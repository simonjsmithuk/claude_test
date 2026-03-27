namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;
using DataViewer.Domain.ValueObjects;

/// <summary>
/// Provides read-only access to AWS S3 buckets using a named credential profile.
/// </summary>
/// <remarks>
/// All S3 operations in DataViewer are read-only (Product Spec § G-04).
/// The infrastructure implementation selects the correct <c>AmazonS3Client</c>
/// keyed by <paramref name="profileId"/> and uses the profile's decrypted AWS
/// credentials — the caller never handles raw AWS keys.
///
/// <para>
/// Implementations must ensure that <see cref="CredentialProfile.EncryptedSecretKey"/>
/// is decrypted in-memory only for the duration of the S3 client construction and
/// is never written to logs, response bodies, or intermediate variables that outlive
/// the scope of the call.
/// </para>
/// </remarks>
public interface IS3Service
{
    /// <summary>
    /// Lists S3 object keys (and their sizes) under an optional key prefix
    /// within the bucket configured on the specified credential profile.
    /// </summary>
    /// <param name="profileId">
    /// The <see cref="CredentialProfile.Id"/> of the credential profile whose
    /// bucket and prefix settings should be used for this listing operation.
    /// </param>
    /// <param name="prefix">
    /// An additional key prefix to narrow the listing within the profile's bucket.
    /// When <see langword="null"/> or empty, the profile's configured
    /// <see cref="CredentialProfile.KeyPrefix"/> is used as the sole prefix.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A read-only list of <see cref="TransactionMetadata"/> records, one per
    /// discovered S3 object, ordered by <see cref="TransactionMetadata.TimestampUtc"/>
    /// descending. Returns an empty list when no objects match the prefix.
    /// </returns>
    Task<IReadOnlyList<TransactionMetadata>> ListObjectsAsync(
        Guid profileId,
        string? prefix,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the raw bytes of a single S3 object identified by its full key.
    /// </summary>
    /// <param name="profileId">
    /// The <see cref="CredentialProfile.Id"/> of the credential profile whose
    /// bucket and AWS credentials should be used to retrieve the object.
    /// </param>
    /// <param name="key">
    /// The full S3 object key of the file to download (e.g.
    /// <c>transactions/2024/01/15/GET_200_api_orders_42.gz</c>).
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The raw (typically gzip-compressed) byte array of the S3 object body.
    /// The caller is responsible for decompression and parsing.
    /// </returns>
    Task<byte[]> GetObjectAsync(
        Guid profileId,
        string key,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tests connectivity and credential validity for the supplied credential profile
    /// by performing a low-cost S3 operation (e.g. <c>ListObjectsV2</c> with
    /// <c>MaxKeys=1</c>) against the configured bucket.
    /// </summary>
    /// <param name="profile">
    /// The fully-populated <see cref="CredentialProfile"/> to test, including the
    /// encrypted secret key that the implementation decrypts in-memory.
    /// The entire profile entity is passed (rather than just its ID) to allow
    /// testing profiles that have not yet been persisted (e.g. during the Create flow).
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// <see langword="true"/> when the connection and credentials are valid and the
    /// bucket is accessible; <see langword="false"/> otherwise.
    /// Implementations must not throw for recoverable connectivity failures — those
    /// should be surfaced as a <see langword="false"/> return so the caller can present
    /// a user-friendly validation error.
    /// </returns>
    Task<bool> TestConnectionAsync(
        CredentialProfile profile,
        CancellationToken cancellationToken);
}
