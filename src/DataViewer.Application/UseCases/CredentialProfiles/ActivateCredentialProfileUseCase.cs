namespace DataViewer.Application.UseCases.CredentialProfiles;

using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates setting a credential profile as the active default for S3 operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single active profile invariant:</b>
/// At most one credential profile may have
/// <see cref="DataViewer.Domain.Entities.CredentialProfile.IsActive"/> set to
/// <see langword="true"/> at any given time. The repository's
/// <see cref="ICredentialProfileRepository.ActivateAsync"/> implementation enforces this
/// invariant atomically within a database transaction by setting the target profile active
/// and clearing <c>IsActive</c> on all other profiles in a single round-trip.
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// An ActivateCredentialProfile audit entry is written BEFORE returning success to the caller,
/// guaranteeing that every profile activation appears in the audit trail.
/// </para>
/// </remarks>
public sealed class ActivateCredentialProfileUseCase
{
    private readonly ICredentialProfileRepository _repository;
    private readonly IAuditService _auditService;

    public ActivateCredentialProfileUseCase(
        ICredentialProfileRepository repository,
        IAuditService auditService)
    {
        _repository = repository;
        _auditService = auditService;
    }

    /// <summary>
    /// Activates a credential profile, making it the system default for S3 operations.
    /// </summary>
    /// <param name="profileId">
    /// The <see cref="DataViewer.Domain.Entities.CredentialProfile.Id"/> to activate.
    /// </param>
    /// <param name="activatedByUserId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the Admin performing the activation,
    /// extracted from the authenticated JWT access token by the API layer.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the profile has been activated and the
    /// audit entry has been committed. No value is returned; the API layer should respond
    /// with <c>HTTP 204 No Content</c> on success.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when no non-deleted profile with the specified <paramref name="profileId"/> exists.
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the ActivateCredentialProfile audit entry cannot be persisted.
    /// The profile is NOT activated in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task ExecuteAsync(
        Guid profileId,
        Guid activatedByUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Load existing profile to ensure it exists and capture name for audit
        var profile = await _repository.GetByIdAsync(profileId, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            throw new NotFoundException(
                "CredentialProfile",
                profileId.ToString());
        }

        // Activate the profile (atomic: sets this profile active, clears all others)
        await _repository.ActivateAsync(profileId, cancellationToken);

        // Audit-first: Write ActivateCredentialProfile audit entry BEFORE returning
        await _auditService.LogCredentialActionAsync(
            activatedByUserId,
            ipAddress,
            AuditActionType.ActivateCredentialProfile,
            profile.Name,
            cancellationToken);

        // Success — no return value (API responds with HTTP 204)
    }
}
