using Amazon;
using Amazon.S3;
using DataViewer.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.S3;

/// <summary>
/// Creates short-lived, profile-scoped <see cref="AmazonS3Client"/> instances from
/// a decrypted <see cref="CredentialProfile"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Singleton registration — safe:</strong>
/// <see cref="S3ClientFactory"/> is stateless beyond its injected logger. It holds no
/// AWS credentials, no <see cref="AmazonS3Client"/> instances, and no mutable state.
/// Registering it as a Singleton is therefore safe and avoids unnecessary allocations
/// on every request.
/// </para>
///
/// <para>
/// <strong>AmazonS3Client is NOT a Singleton:</strong>
/// Each call to <see cref="Create"/> returns a <em>new</em>
/// <see cref="AmazonS3Client"/> constructed from the supplied profile credentials.
/// The caller must dispose the returned client via a <c>using</c> block. This ensures
/// credentials from one profile never bleed into operations for a different profile
/// and that the client is torn down promptly after the S3 operation completes.
/// </para>
///
/// <para>
/// <strong>Secret key logging:</strong>
/// <see cref="Create"/> never logs the <paramref name="decryptedSecretKey"/> parameter
/// under any circumstance. Only the profile ID and Access Key ID (public portion of
/// the credential pair) are emitted in structured log output.
/// </para>
/// </remarks>
internal sealed class S3ClientFactory : IS3ClientFactory
{
    private readonly ILogger<S3ClientFactory> _logger;

    /// <summary>
    /// Initialises the factory with a logger for diagnostic output.
    /// </summary>
    /// <param name="logger">
    /// Structured logger. AWS Secret Access Key material is never written here.
    /// </param>
    public S3ClientFactory(ILogger<S3ClientFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <see cref="RegionEndpoint.GetBySystemName"/> maps the profile's
    /// <see cref="CredentialProfile.Region"/> string (e.g. <c>"eu-west-2"</c>) to the
    /// corresponding <see cref="RegionEndpoint"/> enum value. The AWS SDK accepts any
    /// valid AWS region system name; unknown values fall back to a generated endpoint.
    /// </para>
    ///
    /// <para>
    /// <c>ForcePathStyle = false</c> (the default) uses virtual-hosted–style bucket
    /// addressing (<c>bucket.s3.amazonaws.com</c>), which is the current AWS standard.
    /// Path-style addressing is deprecated by AWS and should not be used for new code.
    /// </para>
    /// </remarks>
    public AmazonS3Client Create(CredentialProfile profile, string decryptedSecretKey)
    {
        ArgumentNullException.ThrowIfNull(profile, nameof(profile));
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.AccessKeyId, nameof(profile.AccessKeyId));
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Region, nameof(profile.Region));
        ArgumentException.ThrowIfNullOrWhiteSpace(decryptedSecretKey, nameof(decryptedSecretKey));

        // Log only the non-sensitive fields: profile ID and Access Key ID (the public
        // portion of the credential pair). The decryptedSecretKey MUST NOT appear here.
        _logger.LogDebug(
            "Creating AmazonS3Client for profile {ProfileId} (AccessKeyId={AccessKeyId}, Region={Region})",
            profile.Id,
            profile.AccessKeyId,
            profile.Region);

        var region = RegionEndpoint.GetBySystemName(profile.Region);

        // Construct a new client with explicit credentials derived from the profile.
        // A new instance is created on every call — NOT a Singleton — so that clients
        // for different profiles are always credential-isolated from one another.
        // The caller disposes the client when the S3 operation is complete.
        return new AmazonS3Client(
            awsAccessKeyId: profile.AccessKeyId,
            awsSecretAccessKey: decryptedSecretKey,   // consumed here; not stored or logged
            region: region);
    }
}
