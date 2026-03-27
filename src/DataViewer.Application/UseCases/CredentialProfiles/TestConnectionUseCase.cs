namespace DataViewer.Application.UseCases.CredentialProfiles;

using DataViewer.Application.DTOs.CredentialProfiles;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates testing of AWS S3 connectivity for a credential profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security invariant:</b>
/// The <see cref="DataViewer.Domain.Entities.CredentialProfile.EncryptedSecretKey"/> is
/// decrypted in-memory for the duration of the S3 connectivity test only. The decrypted
/// plaintext secret is NEVER persisted, logged, or included in any API response — it exists
/// exclusively within the scope of <see cref="IS3Service.TestConnectionAsync"/> and is
/// discarded immediately after (Product Spec § G-04).
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// A TestCredentialProfile audit entry is written REGARDLESS of whether the test succeeds
/// or fails, ensuring that every connectivity attempt is recorded in the audit trail.
/// The audit entry is written BEFORE returning the result to the caller.
/// </para>
/// </remarks>
public sealed class TestConnectionUseCase
{
    private readonly ICredentialProfileRepository _repository;
    private readonly IS3Service _s3Service;
    private readonly IEncryptionService _encryptionService;
    private readonly IAuditService _auditService;

    public TestConnectionUseCase(
        ICredentialProfileRepository repository,
        IS3Service s3Service,
        IEncryptionService encryptionService,
        IAuditService auditService)
    {
        _repository = repository;
        _s3Service = s3Service;
        _encryptionService = encryptionService;
        _auditService = auditService;
    }

    /// <summary>
    /// Tests S3 connectivity for a credential profile.
    /// </summary>
    /// <param name="profileId">
    /// The <see cref="DataViewer.Domain.Entities.CredentialProfile.Id"/> of the profile to test.
    /// </param>
    /// <param name="testedByUserId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the Admin performing the test,
    /// extracted from the authenticated JWT access token by the API layer.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="TestConnectionResultDto"/> indicating whether the test succeeded and
    /// providing a human-readable message describing the outcome.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when no non-deleted profile with the specified <paramref name="profileId"/> exists.
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.EncryptionException">
    /// Thrown when decryption of the secret key fails (fatal configuration error or tampered data).
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the TestCredentialProfile audit entry cannot be persisted.
    /// </exception>
    public async Task<TestConnectionResultDto> ExecuteAsync(
        Guid profileId,
        Guid testedByUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Load existing profile to test
        var profile = await _repository.GetByIdAsync(profileId, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            throw new NotFoundException(
                "CredentialProfile",
                profileId.ToString());
        }

        // Security invariant: Decrypt the secret key in-memory ONLY for the test duration
        // Note: The profile's EncryptedSecretKey is stored as byte[], need to convert to Base64 string
        var encryptedSecretKeyBase64 = Convert.ToBase64String(profile.EncryptedSecretKey);
        var decryptedSecretKey = _encryptionService.Decrypt(encryptedSecretKeyBase64);

        // Create a temporary profile with the decrypted secret for the S3 test
        // The IS3Service.TestConnectionAsync will construct an S3 client with the decrypted credentials
        var testProfile = new DataViewer.Domain.Entities.CredentialProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            AccessKeyId = profile.AccessKeyId,
            EncryptedSecretKey = profile.EncryptedSecretKey,  // S3Service will decrypt this internally
            Region = profile.Region,
            BucketName = profile.BucketName,
            KeyPrefix = profile.KeyPrefix,
            IsActive = profile.IsActive,
            IsDeleted = profile.IsDeleted,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
            CreatedByUserId = profile.CreatedByUserId
        };

        // Perform S3 connectivity test
        bool testSuccess;
        string testMessage;
        var testedAt = DateTime.UtcNow;

        try
        {
            testSuccess = await _s3Service.TestConnectionAsync(testProfile, cancellationToken);

            testMessage = testSuccess
                ? "Connection successful."
                : "Connection failed. Please verify your credentials, region, and bucket name.";
        }
        catch (Exception ex)
        {
            // Catch any exceptions from S3 service and return as failure
            // (exceptions should not propagate to caller - return user-friendly error instead)
            testSuccess = false;
            testMessage = $"Connection test failed: {SanitizeErrorMessage(ex.Message)}";
        }

        // Audit-first: Write TestCredentialProfile audit entry REGARDLESS of success/failure
        await _auditService.LogCredentialActionAsync(
            testedByUserId,
            ipAddress,
            AuditActionType.TestCredentialProfile,
            profile.Name,
            cancellationToken);

        // Return test result
        return new TestConnectionResultDto
        {
            IsSuccess = testSuccess,
            Message = testMessage,
            TestedAt = new DateTimeOffset(testedAt, TimeSpan.Zero)
        };
    }

    /// <summary>
    /// Sanitizes exception messages to prevent leaking sensitive AWS account or region details.
    /// </summary>
    /// <remarks>
    /// AWS SDK exceptions may contain AWS account IDs, ARNs, or other sensitive metadata.
    /// This method removes or redacts such details to produce a safe, Admin-facing error message.
    /// </remarks>
    private static string SanitizeErrorMessage(string rawMessage)
    {
        // Simple sanitization: truncate long messages and remove potential ARNs or account IDs
        // A production implementation should use regex or AWS SDK-specific error handling
        if (rawMessage.Length > 200)
        {
            rawMessage = rawMessage.Substring(0, 200) + "...";
        }

        // Remove common AWS identifiers (basic pattern matching)
        // In production, use more sophisticated sanitization
        rawMessage = System.Text.RegularExpressions.Regex.Replace(
            rawMessage,
            @"arn:aws:[a-z0-9-]+:[a-z0-9-]*:\d+:[^\s]+",
            "[REDACTED_ARN]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        rawMessage = System.Text.RegularExpressions.Regex.Replace(
            rawMessage,
            @"\b\d{12}\b",
            "[REDACTED_ACCOUNT_ID]");

        return rawMessage;
    }
}
