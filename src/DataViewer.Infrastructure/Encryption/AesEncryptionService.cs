using System.Security.Cryptography;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Encryption;

/// <summary>
/// AES-256-GCM symmetric encryption service that satisfies the credential
/// security requirement (Product Spec § G-04): AWS Secret Access Keys are
/// always encrypted at rest and are never returned in any API response.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Algorithm:</strong> AES-256-GCM (Galois/Counter Mode) provides both
/// confidentiality and authenticated integrity via a 128-bit authentication tag,
/// making it the preferred mode over CBC for new implementations per NIST SP 800-38D.
/// The BCL implementation (<see cref="AesGcm"/>) is used directly — no external
/// cryptographic libraries are required.
/// </para>
///
/// <para>
/// <strong>Wire format:</strong> every encrypted value is stored as a Base-64 string
/// whose decoded bytes follow the layout:
/// <code>
/// [nonce: 12 bytes] [ciphertext: N bytes] [tag: 16 bytes]
/// </code>
/// The nonce is <em>prepended</em> rather than stored separately so that a single
/// opaque column value in the database is self-contained and portable.
/// </code>
/// </para>
///
/// <para>
/// <strong>Key management:</strong> the 256-bit (32-byte) master key is read once
/// from the <c>DATAVIEWER_ENCRYPTION_KEY</c> environment variable at service
/// construction time and stored in a private readonly field. It is never written to
/// logs, configuration files, or the network.  The DI lifetime is <em>Singleton</em>
/// — key bytes are held in managed memory for the lifetime of the host process.
/// </para>
///
/// <para>
/// <strong>Nonce uniqueness:</strong> each <see cref="Encrypt"/> call generates a
/// fresh 12-byte nonce via <see cref="RandomNumberGenerator.Fill"/>.  AES-GCM is
/// catastrophically broken when the same (key, nonce) pair is reused; the use of a
/// cryptographically-secure random nonce makes collision probability negligible
/// (2^{-96} per pair per key lifetime).
/// </para>
///
/// <para>
/// <strong>Thread safety:</strong> this class is safe for concurrent use.
/// <see cref="AesGcm"/> is constructed per-operation (Encrypt / Decrypt) rather than
/// shared as a field, so no locking is needed and there is no risk of corrupting
/// internal cipher state across concurrent calls.
/// </para>
/// </remarks>
public sealed class AesEncryptionService : IEncryptionService
{
    // ── AES-GCM constants ────────────────────────────────────────────────────

    /// <summary>
    /// GCM nonce (IV) size in bytes.
    /// NIST SP 800-38D recommends 96 bits (12 bytes) for GCM; this is the
    /// value that yields the best performance and security.
    /// </summary>
    private const int NonceSizeBytes = 12;

    /// <summary>
    /// GCM authentication tag size in bytes (128 bits — the maximum).
    /// AES-GCM supports tag sizes from 4 to 16 bytes; 16 bytes provides the
    /// strongest integrity guarantee.
    /// </summary>
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Expected AES-256 key length in bytes.
    /// The environment variable value must decode to exactly this many bytes;
    /// startup validation enforces the contract before any operation is attempted.
    /// </summary>
    private const int KeySizeBytes = 32;

    // ── Environment variable name ────────────────────────────────────────────

    /// <summary>
    /// Name of the environment variable from which the 32-byte Base-64 key is read.
    /// </summary>
    /// <remarks>
    /// This constant is used <em>only</em> for validation error messages and log
    /// messages.  The value of the environment variable is <strong>never</strong>
    /// written to any log output — only its presence/absence and the decoded byte
    /// count are logged.
    /// </remarks>
    internal const string EncryptionKeyEnvVar = "DATAVIEWER_ENCRYPTION_KEY";

    // ── Private fields ───────────────────────────────────────────────────────

    /// <summary>
    /// The 32-byte AES-256 key decoded at startup.
    /// Stored as a byte array rather than a string to facilitate zeroing
    /// (if <see cref="Dispose"/> is added in future) and to avoid repeated
    /// Base-64 decoding on every call.
    /// </summary>
    private readonly byte[] _keyBytes;

    private readonly ILogger<AesEncryptionService> _logger;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the service, reading and validating the AES key from the
    /// <c>DATAVIEWER_ENCRYPTION_KEY</c> environment variable.
    /// </summary>
    /// <param name="logger">
    /// Logger for diagnostic messages. Key material is never written to the log.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when <c>DATAVIEWER_ENCRYPTION_KEY</c> is not set, is empty,
    /// is not valid Base-64, or decodes to a byte array that is not exactly
    /// <see cref="KeySizeBytes"/> (32) bytes. This is intentionally a fatal startup
    /// error — the application must not continue without a valid encryption key
    /// because doing so would silently store unencrypted credentials.
    /// </exception>
    public AesEncryptionService(ILogger<AesEncryptionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _keyBytes = LoadAndValidateKey();

        // Log only that the key was loaded successfully — never its value.
        _logger.LogInformation(
            "AES-256-GCM encryption key loaded successfully ({KeySizeBytes}-byte key)",
            KeySizeBytes);
    }

    // ── IEncryptionService implementation ────────────────────────────────────

    /// <inheritdoc />
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> using AES-256-GCM with a freshly
    /// generated random 12-byte nonce.
    /// </summary>
    /// <returns>
    /// Base-64 string whose decoded bytes are:
    /// <c>[nonce: 12 bytes] [ciphertext: N bytes] [tag: 16 bytes]</c>
    /// </returns>
    /// <exception cref="EncryptionException">
    /// Wraps any <see cref="CryptographicException"/> thrown by the BCL so that
    /// callers deal with a single well-typed domain exception.
    /// </exception>
    public string Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext, nameof(plaintext));

        try
        {
            var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

            // Allocate output buffers.
            var nonce = new byte[NonceSizeBytes];
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSizeBytes];

            // Generate a cryptographically-secure random nonce for this operation.
            // MUST be unique per (key, message) pair — RandomNumberGenerator provides this.
            RandomNumberGenerator.Fill(nonce);

            // AesGcm is constructed per-call (not shared as a field) to guarantee
            // thread safety and avoid any risk of nonce reuse through shared state.
            using var aesGcm = new AesGcm(_keyBytes, TagSizeBytes);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Concatenate [nonce || ciphertext || tag] into a single byte array,
            // then Base-64 encode for safe string storage in the database.
            var combined = CombineSegments(nonce, ciphertext, tag);
            return Convert.ToBase64String(combined);
        }
        catch (CryptographicException ex)
        {
            // Log at Error level — the exception detail stays server-side; never
            // surfaced to API callers. Key material is intentionally absent from the message.
            _logger.LogError(ex,
                "AES-256-GCM encrypt operation failed. OperationContext={OperationContext}",
                "Encrypt");

            throw new EncryptionException(
                message: "Encryption operation failed. See server logs for details.",
                operationContext: "Encrypt",
                innerException: ex);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Decrypts a Base-64 encoded value previously produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="ciphertext">
    /// Base-64 string whose decoded bytes must follow the layout
    /// <c>[nonce: 12 bytes] [ciphertext: N bytes] [tag: 16 bytes]</c>.
    /// </param>
    /// <returns>The original plaintext string.</returns>
    /// <exception cref="EncryptionException">
    /// Thrown when:
    /// <list type="bullet">
    ///   <item><description>The input is not valid Base-64.</description></item>
    ///   <item><description>The decoded bytes are too short to contain a nonce and tag.</description></item>
    ///   <item><description>Authentication tag verification fails (tampered ciphertext or wrong key).</description></item>
    /// </list>
    /// In all cases the underlying <see cref="CryptographicException"/> is preserved as
    /// the inner exception for server-side diagnostics.
    /// </exception>
    public string Decrypt(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext, nameof(ciphertext));

        byte[] combined;

        try
        {
            combined = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex,
                "AES-256-GCM decrypt failed: input is not valid Base-64. OperationContext={OperationContext}",
                "Decrypt");

            // Wrap in a CryptographicException so the catch block below can handle it
            // uniformly, preserving the original FormatException as inner cause.
            throw new EncryptionException(
                message: "Decryption failed: the stored value is not valid Base-64.",
                operationContext: "Decrypt",
                innerException: ex);
        }

        // Minimum valid length: nonce (12) + at least 0 bytes of ciphertext + tag (16).
        // An empty plaintext produces a zero-length ciphertext which is valid GCM output.
        int minimumLength = NonceSizeBytes + TagSizeBytes;
        if (combined.Length < minimumLength)
        {
            _logger.LogError(
                "AES-256-GCM decrypt failed: encoded payload too short " +
                "(got {ActualBytes} bytes, minimum {MinimumBytes}). OperationContext={OperationContext}",
                combined.Length, minimumLength, "Decrypt");

            throw new EncryptionException(
                message: "Decryption failed: the stored value is malformed (payload too short).",
                operationContext: "Decrypt");
        }

        try
        {
            // Parse the concatenated layout: [nonce || ciphertext || tag]
            int ciphertextLength = combined.Length - NonceSizeBytes - TagSizeBytes;

            var nonce      = combined.AsSpan(0, NonceSizeBytes);
            var cipherBytes = combined.AsSpan(NonceSizeBytes, ciphertextLength);
            var tag        = combined.AsSpan(NonceSizeBytes + ciphertextLength, TagSizeBytes);

            var plaintextBytes = new byte[ciphertextLength];

            // AesGcm is constructed per-call for thread safety (no shared mutable state).
            using var aesGcm = new AesGcm(_keyBytes, TagSizeBytes);

            // Decrypt() throws CryptographicException if the authentication tag does
            // not match — this is the tamper/wrong-key detection path.
            aesGcm.Decrypt(nonce, cipherBytes, tag, plaintextBytes);

            return System.Text.Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            // Tag mismatch is the most common cause here. The error message is
            // deliberately vague to avoid leaking information to a potential attacker
            // who might observe error distinctions. Full detail is in the server log.
            _logger.LogError(ex,
                "AES-256-GCM decrypt operation failed (possible tag mismatch or wrong key). " +
                "OperationContext={OperationContext}",
                "Decrypt");

            throw new EncryptionException(
                message: "Decryption failed: authentication tag verification error. " +
                         "The data may have been tampered with or the encryption key has changed.",
                operationContext: "Decrypt",
                innerException: ex);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>DATAVIEWER_ENCRYPTION_KEY</c> environment variable, Base-64 decodes
    /// it, and validates that the result is exactly <see cref="KeySizeBytes"/> bytes.
    /// </summary>
    /// <returns>The decoded 32-byte AES key.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the variable is missing, empty, invalid Base-64, or the wrong length.
    /// The exception message identifies the problem category without revealing key material.
    /// </exception>
    private static byte[] LoadAndValidateKey()
    {
        // Read from the environment — not from IConfiguration — as per the task spec.
        // This ensures the key is never accidentally written to appsettings.json or
        // logged as part of the configuration dump.
        var rawValue = Environment.GetEnvironmentVariable(EncryptionKeyEnvVar);

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{EncryptionKeyEnvVar}' is not set or is empty. " +
                $"Provide a Base-64 encoded 32-byte (256-bit) AES key.");
        }

        byte[] keyBytes;

        try
        {
            keyBytes = Convert.FromBase64String(rawValue);
        }
        catch (FormatException ex)
        {
            // Wrap in InvalidOperationException to surface a clear startup error.
            // The original FormatException is preserved as inner exception for diagnostics
            // but the raw value is intentionally NOT included in either message.
            throw new InvalidOperationException(
                $"Environment variable '{EncryptionKeyEnvVar}' is not valid Base-64. " +
                $"Ensure the value is a Base-64 encoded 32-byte key with no padding issues.",
                ex);
        }

        if (keyBytes.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Environment variable '{EncryptionKeyEnvVar}' decoded to {keyBytes.Length} bytes " +
                $"but exactly {KeySizeBytes} bytes are required for AES-256. " +
                $"Generate a valid key with: " +
                $"openssl rand -base64 32");
        }

        return keyBytes;
    }

    /// <summary>
    /// Allocates a single byte array and copies <paramref name="nonce"/>,
    /// <paramref name="ciphertext"/>, and <paramref name="tag"/> into it
    /// in order, producing the canonical storage layout.
    /// </summary>
    /// <returns>
    /// A new byte array containing <c>[nonce || ciphertext || tag]</c>.
    /// </returns>
    private static byte[] CombineSegments(byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];

        var destination = combined.AsSpan();
        nonce.AsSpan().CopyTo(destination);
        destination = destination[nonce.Length..];

        ciphertext.AsSpan().CopyTo(destination);
        destination = destination[ciphertext.Length..];

        tag.AsSpan().CopyTo(destination);

        return combined;
    }
}
