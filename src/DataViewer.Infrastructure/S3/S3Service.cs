namespace DataViewer.Infrastructure.S3;

using System;
using Amazon.S3;
using Amazon.S3.Model;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// AWS S3 service implementation providing read-only access to S3 buckets.
/// </summary>
/// <remarks>
/// This implementation creates AmazonS3Client instances on-demand using the
/// decrypted credentials from the specified CredentialProfile.
/// All S3 operations are read-only (Product Spec § G-04).
/// </remarks>
public sealed class S3Service : IS3Service
{
    private readonly ICredentialProfileRepository _profileRepository;
    private readonly IEncryptionService _encryptionService;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly ILogger<S3Service> _logger;

    public S3Service(
        ICredentialProfileRepository profileRepository,
        IEncryptionService encryptionService,
        IMetadataExtractor metadataExtractor,
        ILogger<S3Service> logger)
    {
        _profileRepository = profileRepository;
        _encryptionService = encryptionService;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransactionMetadata>> ListObjectsAsync(
        Guid profileId,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId, cancellationToken);
        if (profile is null)
        {
            _logger.LogWarning("Credential profile {ProfileId} not found", profileId);
            return Array.Empty<TransactionMetadata>();
        }

        var client = CreateS3Client(profile);

        try
        {
            var effectivePrefix = string.IsNullOrEmpty(prefix)
                ? profile.KeyPrefix
                : $"{profile.KeyPrefix?.TrimEnd('/')}/{prefix.TrimStart('/')}";

            var request = new ListObjectsV2Request
            {
                BucketName = profile.BucketName,
                Prefix = effectivePrefix
            };

            var response = await client.ListObjectsV2Async(request, cancellationToken);

            var results = new List<TransactionMetadata>();
            foreach (var s3Object in response.S3Objects)
            {
                try
                {
                    // Extract metadata from S3 object key using the configured extractor
                    var metadata = _metadataExtractor.ExtractFromKey(s3Object.Key);
                    results.Add(metadata);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract metadata from S3 key: {Key}", s3Object.Key);
                    // Skip objects that don't conform to expected naming convention
                }
            }

            // Sort by timestamp descending (most recent first)
            return results.OrderByDescending(m => m.TimestampUtc).ToList();
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error while listing objects for profile {ProfileId}: {ErrorCode}",
                profileId, ex.ErrorCode);
            throw;
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GetObjectAsync(
        Guid profileId,
        string key,
        CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId, cancellationToken);
        if (profile is null)
        {
            throw new InvalidOperationException($"Credential profile {profileId} not found");
        }

        var client = CreateS3Client(profile);

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = profile.BucketName,
                Key = key
            };

            using var response = await client.GetObjectAsync(request, cancellationToken);
            using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error while getting object {Key} for profile {ProfileId}: {ErrorCode}",
                key, profileId, ex.ErrorCode);
            throw;
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(
        CredentialProfile profile,
        CancellationToken cancellationToken)
    {
        var client = CreateS3Client(profile);

        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = profile.BucketName,
                Prefix = profile.KeyPrefix,
                MaxKeys = 1
            };

            await client.ListObjectsV2Async(request, cancellationToken);

            _logger.LogInformation("S3 connection test successful for profile {ProfileName}",
                profile.Name);
            return true;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 connection test failed for profile {ProfileName}: {ErrorCode}",
                profile.Name, ex.ErrorCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during S3 connection test for profile {ProfileName}",
                profile.Name);
            return false;
        }
        finally
        {
            client?.Dispose();
        }
    }

    private AmazonS3Client CreateS3Client(CredentialProfile profile)
    {
        // Convert byte[] to base64 string for decryption
        var base64EncryptedKey = Convert.ToBase64String(profile.EncryptedSecretKey);

        // Decrypt the secret key in-memory only for the duration of client construction
        var decryptedSecretKey = _encryptionService.Decrypt(base64EncryptedKey);

        var credentials = new Amazon.Runtime.BasicAWSCredentials(
            profile.AccessKeyId,
            decryptedSecretKey);

        var config = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(profile.Region)
        };

        return new AmazonS3Client(credentials, config);
    }
}
