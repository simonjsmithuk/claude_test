namespace DataViewer.Application.Interfaces;

/// <summary>
/// Provides symmetric encryption and decryption of sensitive string values using
/// AES-256-CBC (Product Spec § G-04: AWS credentials encrypted at rest).
/// </summary>
/// <remarks>
/// The infrastructure implementation derives the AES key from application configuration
/// (never from a hardcoded value) and prepends the randomly-generated IV to the
/// ciphertext output as a <c>[<see cref="DataViewer.Domain.Entities.CredentialProfile.IvSizeBytes"/>-byte IV] + [ciphertext]</c>
/// byte sequence, which is then Base-64 encoded for safe string transport.
///
/// <para>
/// This interface is intentionally synchronous: AES encryption and decryption are
/// CPU-bound in-memory operations for which async overhead is unwarranted.
/// </para>
///
/// <para>
/// Callers must treat the return value of <see cref="Decrypt"/> as sensitive data and
/// ensure it is used only within the minimum necessary scope (e.g. constructing an
/// AWS client) before being discarded. It must never be written to logs, response
/// bodies, or long-lived fields.
/// </para>
/// </remarks>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts the supplied plaintext string using AES-256-CBC.
    /// </summary>
    /// <param name="plaintext">
    /// The sensitive value to encrypt (e.g. an AWS Secret Access Key).
    /// Must not be <see langword="null"/> or empty; callers should validate before
    /// invoking this method.
    /// </param>
    /// <returns>
    /// A Base-64 encoded string containing the prepended IV followed by the ciphertext,
    /// suitable for persistence in the database.
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.EncryptionException">
    /// Thrown when the underlying cryptographic operation fails (e.g. invalid key
    /// configuration). Callers should treat this as a fatal configuration error.
    /// </exception>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a Base-64 encoded ciphertext string previously produced by
    /// <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="ciphertext">
    /// The Base-64 encoded string to decrypt.
    /// Must not be <see langword="null"/> or empty.
    /// </param>
    /// <returns>
    /// The original plaintext string recovered from the ciphertext.
    /// The caller is responsible for treating this value as sensitive and
    /// discarding it after use.
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.EncryptionException">
    /// Thrown when decryption fails (e.g. tampered ciphertext, incorrect key,
    /// invalid Base-64 input). Callers should treat this as a security error
    /// and log it appropriately without exposing the exception detail to end users.
    /// </exception>
    string Decrypt(string ciphertext);
}
