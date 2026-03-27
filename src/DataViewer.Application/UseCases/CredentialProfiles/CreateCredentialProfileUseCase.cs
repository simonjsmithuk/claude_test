namespace DataViewer.Application.UseCases.CredentialProfiles;

using System.Text;
using DataViewer.Application.DTOs.CredentialProfiles;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates the creation of a new AWS credential profile for accessing S3 transaction records.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security invariants enforced by this use case:</b>
/// <list type="number">
///   <item>
///     <description>
///       The raw <see cref="CreateCredentialProfileDto.SecretAccessKey"/> is encrypted using
///       <see cref="IEncryptionService"/> BEFORE being stored in the
///       <see cref="CredentialProfile.EncryptedSecretKey"/> field. The raw secret is never
///       persisted in plain text.
///     </description>
///   </item>
///   <item>
///     <description>
///       The returned <see cref="CredentialProfileDto"/> does NOT include the
///       <c>SecretAccessKey</c> or <c>EncryptedSecretKey</c> fields. The secret is returned
///       to the client in the CREATE request exactly once and is never retrievable again.
///     </description>
///   </item>
///   <item>
///     <description>
///       A CreateCredentialProfile audit entry is written BEFORE returning the DTO,
///       implementing the audit-first guarantee (ADR-009).
///     </description>
///   </item>
///   <item>
///     <description>
///       When <see cref="CreateCredentialProfileDto.SetAsActive"/> is <see langword="true"/>,
///       the new profile is atomically activated (any existing active profile is deactivated)
///       within the same database transaction.
///     </description>
///   </item>
/// </list>
/// </para>
/// </remarks>
public sealed class CreateCredentialProfileUseCase
{
    private readonly ICredentialProfileRepository _repository;
    private readonly IEncryptionService _encryptionService;
    private readonly IAuditService _auditService;

    public CreateCredentialProfileUseCase(
        ICredentialProfileRepository repository,
        IEncryptionService encryptionService,
        IAuditService auditService)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _auditService = auditService;
    }

    /// <summary>
    /// Creates a new credential profile with encrypted AWS credentials.
    /// </summary>
    /// <param name="request">The credential profile creation request from the Admin.</param>
    /// <param name="createdByUserId">
    /// The <see cref="User.Id"/> of the Admin creating this profile, extracted from the
    /// authenticated JWT access token by the API layer.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="CredentialProfileDto"/> representing the newly created profile.
    /// The secret key is NOT included in this response.
    /// </returns>
    /// <exception cref="DuplicateNameException">
    /// Thrown when a non-deleted profile with the same <see cref="CreateCredentialProfileDto.Name"/>
    /// already exists.
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.EncryptionException">
    /// Thrown when the encryption of the secret key fails (fatal configuration error).
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the CreateCredentialProfile audit entry cannot be persisted.
    /// The profile is NOT saved in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task<CredentialProfileDto> ExecuteAsync(
        CreateCredentialProfileDto request,
        Guid createdByUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Check for duplicate profile name (case-insensitive uniqueness)
        var existingProfiles = await _repository.GetAllAsync(
            includeDeleted: false,
            cancellationToken);

        if (existingProfiles.Any(p => p.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateNameException(
                $"A credential profile with the name '{request.Name}' already exists.");
        }

        // Security invariant #1: Encrypt the secret access key BEFORE persistence
        var encryptedSecretKey = _encryptionService.Encrypt(request.SecretAccessKey);

        // Convert encrypted string (Base64) to byte array for storage
        var encryptedSecretKeyBytes = Convert.FromBase64String(encryptedSecretKey);

        // Create the domain entity
        var profile = new CredentialProfile
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            AccessKeyId = request.AccessKeyId,
            EncryptedSecretKey = encryptedSecretKeyBytes,
            Region = request.Region,
            BucketName = request.BucketName,
            KeyPrefix = request.KeyPrefix,
            IsActive = false,  // Will be set via ActivateAsync if SetAsActive is true
            IsDeleted = false,
            CreatedByUserId = createdByUserId
            // CreatedAt and UpdatedAt are set by Infrastructure layer interceptor
        };

        // Persist the profile
        await _repository.CreateAsync(profile, cancellationToken);

        // Security invariant #4: Activate if requested (atomic operation)
        if (request.SetAsActive)
        {
            await _repository.ActivateAsync(profile.Id, cancellationToken);
            profile.IsActive = true;  // Update in-memory entity for DTO mapping
        }

        // Security invariant #3: Write CreateCredentialProfile audit entry BEFORE returning
        await _auditService.LogCredentialActionAsync(
            createdByUserId,
            ipAddress,
            AuditActionType.CreateCredentialProfile,
            profile.Name,
            cancellationToken);

        // Security invariant #2: Map to DTO WITHOUT exposing the secret key
        return MapToDto(profile);
    }

    /// <summary>
    /// Maps a <see cref="CredentialProfile"/> domain entity to a <see cref="CredentialProfileDto"/>
    /// for API responses.
    /// </summary>
    /// <remarks>
    /// <b>Security enforcement point:</b> This method deliberately omits the
    /// <see cref="CredentialProfile.EncryptedSecretKey"/> field from the output DTO.
    /// The AWS Secret Access Key is NEVER returned in any API response under any circumstances
    /// (Product Spec § G-04).
    /// </remarks>
    private static CredentialProfileDto MapToDto(CredentialProfile profile)
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
