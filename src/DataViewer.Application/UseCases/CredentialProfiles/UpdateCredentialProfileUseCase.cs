namespace DataViewer.Application.UseCases.CredentialProfiles;

using DataViewer.Application.DTOs.CredentialProfiles;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates partial or full updates to an existing AWS credential profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Partial update semantics:</b>
/// All fields in <see cref="UpdateCredentialProfileDto"/> are nullable.
/// A <see langword="null"/> value means "do not change this field" — only non-null fields
/// are applied to the domain entity. This allows clients to update a single field
/// (e.g. just the bucket name) without having to re-supply all other fields.
/// </para>
///
/// <para>
/// <b>Secret key re-encryption (security invariant):</b>
/// When <see cref="UpdateCredentialProfileDto.SecretAccessKey"/> is non-null, the new
/// plaintext secret is encrypted via <see cref="IEncryptionService"/> and overwrites the
/// stored <see cref="DataViewer.Domain.Entities.CredentialProfile.EncryptedSecretKey"/>.
/// When <see langword="null"/>, the existing encrypted secret is left unchanged — this
/// allows updating other profile fields without re-supplying the secret (which the Admin
/// cannot retrieve).
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// An UpdateCredentialProfile audit entry is written BEFORE the database update is
/// committed, guaranteeing that every profile modification appears in the audit trail.
/// </para>
/// </remarks>
public sealed class UpdateCredentialProfileUseCase
{
    private readonly ICredentialProfileRepository _repository;
    private readonly IEncryptionService _encryptionService;
    private readonly IAuditService _auditService;

    public UpdateCredentialProfileUseCase(
        ICredentialProfileRepository repository,
        IEncryptionService encryptionService,
        IAuditService auditService)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _auditService = auditService;
    }

    /// <summary>
    /// Updates an existing credential profile with the supplied non-null fields.
    /// </summary>
    /// <param name="profileId">The <see cref="DataViewer.Domain.Entities.CredentialProfile.Id"/> to update.</param>
    /// <param name="request">The partial update request containing only the fields to change.</param>
    /// <param name="updatedByUserId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the Admin performing the update,
    /// extracted from the authenticated JWT access token by the API layer.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="CredentialProfileDto"/> representing the updated profile.
    /// The secret key is NOT included in this response.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when no non-deleted profile with the specified <paramref name="profileId"/> exists.
    /// </exception>
    /// <exception cref="DuplicateNameException">
    /// Thrown when <see cref="UpdateCredentialProfileDto.Name"/> is non-null and another
    /// non-deleted profile with that name already exists.
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.EncryptionException">
    /// Thrown when re-encryption of the secret key fails (fatal configuration error).
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the UpdateCredentialProfile audit entry cannot be persisted.
    /// The profile is NOT updated in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task<CredentialProfileDto> ExecuteAsync(
        Guid profileId,
        UpdateCredentialProfileDto request,
        Guid updatedByUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Load existing profile (excludes soft-deleted profiles)
        var profile = await _repository.GetByIdAsync(profileId, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            throw new NotFoundException(
                $"Credential profile with ID '{profileId}' not found.");
        }

        // Check for duplicate name if Name is being updated
        if (request.Name is not null &&
            !request.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            var existingProfiles = await _repository.GetAllAsync(
                includeDeleted: false,
                cancellationToken);

            if (existingProfiles.Any(p =>
                p.Id != profileId &&
                p.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DuplicateNameException(
                    $"A credential profile with the name '{request.Name}' already exists.");
            }
        }

        // Apply partial updates (only non-null fields)
        if (request.Name is not null)
        {
            profile.Name = request.Name;
        }

        if (request.AccessKeyId is not null)
        {
            profile.AccessKeyId = request.AccessKeyId;
        }

        // Security invariant: Re-encrypt secret ONLY if a new value is provided
        if (request.SecretAccessKey is not null)
        {
            var encryptedSecretKey = _encryptionService.Encrypt(request.SecretAccessKey);
            profile.EncryptedSecretKey = Convert.FromBase64String(encryptedSecretKey);
        }

        if (request.Region is not null)
        {
            profile.Region = request.Region;
        }

        if (request.BucketName is not null)
        {
            profile.BucketName = request.BucketName;
        }

        // Three-state KeyPrefix handling: null = no-op, "" = clear, non-empty = set
        if (request.KeyPrefix is not null)
        {
            profile.KeyPrefix = string.IsNullOrEmpty(request.KeyPrefix) ? null : request.KeyPrefix;
        }

        // Activate if requested (atomic operation)
        if (request.SetAsActive == true)
        {
            await _repository.ActivateAsync(profile.Id, cancellationToken);
            profile.IsActive = true;  // Update in-memory entity for DTO mapping
        }

        // Persist the updated profile
        await _repository.UpdateAsync(profile, cancellationToken);

        // Audit-first: Write UpdateCredentialProfile audit entry BEFORE returning
        await _auditService.LogCredentialActionAsync(
            updatedByUserId,
            ipAddress,
            AuditActionType.UpdateCredentialProfile,
            profile.Name,
            cancellationToken);

        // Map to DTO WITHOUT exposing the secret key
        return MapToDto(profile);
    }

    /// <summary>
    /// Maps a <see cref="DataViewer.Domain.Entities.CredentialProfile"/> domain entity
    /// to a <see cref="CredentialProfileDto"/> for API responses.
    /// </summary>
    /// <remarks>
    /// <b>Security enforcement point:</b> This method deliberately omits the
    /// <see cref="DataViewer.Domain.Entities.CredentialProfile.EncryptedSecretKey"/> field
    /// from the output DTO. The AWS Secret Access Key is NEVER returned in any API response
    /// under any circumstances (Product Spec § G-04).
    /// </remarks>
    private static CredentialProfileDto MapToDto(DataViewer.Domain.Entities.CredentialProfile profile)
    {
        return new CredentialProfileDto
        {
            Id = profile.Id,
            Name = profile.Name,
            AccessKeyId = profile.AccessKeyId,
            Region = profile.Region,
            BucketName = profile.BucketName,
            KeyPrefix = profile.KeyPrefix,
            IsActive = profile.IsActive,
            IsDeleted = profile.IsDeleted,
            CreatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc),
                TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc),
                TimeSpan.Zero)
        };
    }
}
