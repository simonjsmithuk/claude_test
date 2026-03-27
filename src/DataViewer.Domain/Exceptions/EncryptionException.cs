namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an encryption or decryption operation fails within the DataViewer
/// cryptographic subsystem (AES-256-GCM used for AWS Secret Access Key storage).
/// </summary>
/// <remarks>
/// Maps to HTTP 500 Internal Server Error in the API layer; the underlying
/// cryptographic details are logged server-side but must never be surfaced to
/// the client in order to avoid leaking key-management information.
/// <para>
/// <see cref="OperationContext"/> identifies which specific cryptographic step
/// failed, enabling precise error attribution in logs without exposing sensitive
/// material. Expected values include <c>"Encrypt"</c>, <c>"Decrypt"</c>, and
/// <c>"KeyDerivation"</c>.
/// </para>
/// </remarks>
public sealed class EncryptionException : DomainException
{
    /// <summary>
    /// Initialises the exception with a description of the cryptographic failure
    /// and the operation that was being performed.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of the encryption failure.
    /// Must not include raw key material, plaintext, or ciphertext.
    /// </param>
    /// <param name="operationContext">
    /// Short label identifying which cryptographic step failed
    /// (e.g. <c>"Encrypt"</c>, <c>"Decrypt"</c>, <c>"KeyDerivation"</c>).
    /// </param>
    public EncryptionException(string message, string operationContext)
        : base(message)
    {
        OperationContext = operationContext;
    }

    /// <summary>
    /// Initialises the exception with a description of the cryptographic failure,
    /// the operation that was being performed, and the underlying cause.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of the encryption failure.
    /// Must not include raw key material, plaintext, or ciphertext.
    /// </param>
    /// <param name="operationContext">
    /// Short label identifying which cryptographic step failed
    /// (e.g. <c>"Encrypt"</c>, <c>"Decrypt"</c>, <c>"KeyDerivation"</c>).
    /// </param>
    /// <param name="innerException">
    /// The lower-level cryptographic exception (e.g. <see cref="System.Security.Cryptography.CryptographicException"/>)
    /// that caused this fault. Logged server-side; never forwarded to API responses.
    /// </param>
    public EncryptionException(string message, string operationContext, Exception innerException)
        : base(message, innerException)
    {
        OperationContext = operationContext;
    }

    // ── Cryptographic-context properties ────────────────────────────────────

    /// <summary>
    /// Short label identifying which cryptographic operation was in progress when
    /// the failure occurred. Included in structured log output to aid diagnosis
    /// without disclosing sensitive information.
    /// <para>
    /// Expected values:
    /// <list type="bullet">
    ///   <item><description><c>Encrypt</c> — AES-GCM encryption of a Secret Access Key before persistence.</description></item>
    ///   <item><description><c>Decrypt</c> — AES-GCM decryption of a stored Secret Access Key before use.</description></item>
    ///   <item><description><c>KeyDerivation</c> — Derivation of the AES key from the configured master secret.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public string OperationContext { get; }
}
