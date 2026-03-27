using Amazon.S3;
using DataViewer.Domain.Entities;

namespace DataViewer.Infrastructure.S3;

/// <summary>
/// Infrastructure-internal factory that constructs a short-lived
/// <see cref="AmazonS3Client"/> from the decrypted credentials and region on a
/// <see cref="CredentialProfile"/>.
/// </summary>
/// <remarks>
/// <para>
/// This interface is declared in the Infrastructure layer (not Application) because
/// it references <see cref="AmazonS3Client"/>, which is an AWS SDK type and therefore
/// an Infrastructure-layer detail. The Application layer knows only
/// <see cref="DataViewer.Application.Interfaces.IS3Service"/>; it has no knowledge
/// of how an S3 client is constructed.
/// </para>
///
/// <para>
/// <strong>Why a factory rather than a Singleton:</strong>
/// Each <see cref="CredentialProfile"/> carries its own AWS credentials (Access Key ID,
/// Secret Access Key, and Region). A Singleton <see cref="AmazonS3Client"/> would be
/// locked to one fixed credential set, making it impossible to serve multiple profiles
/// or to test credential validity before persisting a new profile. The factory pattern
/// ensures each S3 operation receives a client configured for exactly the profile being
/// used, and that the client is disposed immediately after the operation.
/// </para>
///
/// <para>
/// <strong>Caller responsibility:</strong>
/// The caller <strong>must</strong> dispose the returned <see cref="AmazonS3Client"/>
/// after the S3 operation completes. Use a <c>using</c> block to guarantee disposal
/// even when exceptions are thrown:
/// <code>
/// using var client = _factory.Create(profile, decryptedSecret);
/// </code>
/// </para>
/// </remarks>
internal interface IS3ClientFactory
{
    /// <summary>
    /// Creates a new <see cref="AmazonS3Client"/> configured with the credentials
    /// and region from the supplied credential profile.
    /// </summary>
    /// <param name="profile">
    /// The credential profile providing <see cref="CredentialProfile.AccessKeyId"/>,
    /// <see cref="CredentialProfile.Region"/>, and
    /// <see cref="CredentialProfile.BucketName"/>.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="decryptedSecretKey">
    /// The plaintext AWS Secret Access Key, already decrypted from
    /// <see cref="CredentialProfile.EncryptedSecretKey"/> by the caller.
    /// This value is consumed only within the <see cref="AmazonS3Client"/> constructor
    /// and is never written to any log, field, or persistent store.
    /// Must not be <see langword="null"/> or whitespace.
    /// </param>
    /// <returns>
    /// A new, fully-configured <see cref="AmazonS3Client"/> ready for use.
    /// The caller <strong>must</strong> dispose the returned client when done
    /// (preferably via a <c>using</c> block).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="decryptedSecretKey"/> is <see langword="null"/> or
    /// whitespace, or when the profile's <see cref="CredentialProfile.AccessKeyId"/>
    /// or <see cref="CredentialProfile.Region"/> are null or empty.
    /// </exception>
    AmazonS3Client Create(CredentialProfile profile, string decryptedSecretKey);
}
