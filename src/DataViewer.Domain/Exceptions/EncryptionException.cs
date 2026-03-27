namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an encryption or decryption operation fails within the DataViewer
/// cryptographic subsystem (AES-256-GCM, used for securing AWS Secret Access Keys
/// at rest).
/// </summary>
/// <remarks>
/// Maps to <c>HTTP 500 Internal Server Error</c> in the API layer. The underlying
/// cryptographic details are captured in structured server-side logs but must
/// <b>never</b> be surfaced to the API consumer — doing so would risk leaking
/// key-management information.
/// <para>
/// <see cref="OperationContext"/> identifies which specific cryptographic step
/// failed, enabling precise error attribution in logs without exposing sensitive
/// material. Expected values are <c>"Encrypt"</c>, <c>"Decrypt"</c>, and
/// <c>"KeyDerivation"</c>.
/// </para>
/// <para>
/// The inner exception (when present) will typically be a
/// <see cref="System.Security.Cryptography.CryptographicException"/> from the BCL.
/// It must be preserved for server-side diagnostics but not forwarded to callers.
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
    /// Must not contain raw key material, plaintext, or ciphertext values.
    /// </param>
    /// <param name="operationContext">
    /// Short label identifying which cryptographic step failed.
    /// Expected values: <c>"Encrypt"</c>, <c>"Decrypt"</c>, <c>"KeyDerivation"</c>.
    /// </param>
    public EncryptionException(string message, string operationContext)
        : base(message)
    {
        OperationContext = operationContext;
    }

    /// <summary>
    /// Initialises the exception with a description of the cryptographic failure,
    /// the operation that was being performed, and the underlying root cause.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of the encryption failure.
    /// Must not contain raw key material, plaintext, or ciphertext values.
    /// </param>
    /// <param name="operationContext">
    /// Short label identifying which cryptographic step failed.
    /// Expected values: <c>"Encrypt"</c>, <c>"Decrypt"</c>, <c>"KeyDerivation"</c>.
    /// </param>
    /// <param name="innerException">
    /// The lower-level cryptographic exception (typically
    /// <see cref="System.Security.Cryptography.CryptographicException"/>)
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
    /// the failure occurred.
    /// <para>
    /// Expected values:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>Encrypt</c> — AES-256-GCM encryption of a Secret Access Key
    ///       before it is persisted to the database.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>Decrypt</c> — AES-256-GCM decryption of a stored Secret Access Key
    ///       before it is used to sign an S3 request.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>KeyDerivation</c> — Derivation of the AES encryption key from the
    ///       configured master secret via PBKDF2 or HKDF.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public string OperationContext { get; }
}
