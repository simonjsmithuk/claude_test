namespace DataViewer.Application.UseCases.CredentialProfiles;

using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates the soft-deletion of an AWS credential profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Soft-delete semantics:</b>
/// Profiles are never hard-deleted from the database. Instead,
/// <see cref="DataViewer.Domain.Entities.CredentialProfile.IsDeleted"/> is set to
/// <see langword="true"/>, preserving the row for audit log integrity. Soft-deleted
/// profiles are excluded from all operational queries but remain accessible for
/// audit-log reconciliation (Product Spec § G-03).
/// </para>
///
/// <para>
/// <b>Active profile handling:</b>
/// If the profile being deleted is currently active
/// (<see cref="DataViewer.Domain.Entities.CredentialProfile.IsActive"/> = <see langword="true"/>),
/// the repository's <see cref="ICredentialProfileRepository.SoftDeleteAsync"/> implementation
/// atomically clears <c>IsActive</c> alongside setting <c>IsDeleted</c> to prevent a deleted
/// profile from remaining the system default.
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// A DeleteCredentialProfile audit entry is written BEFORE the database soft-delete is
/// committed, guaranteeing that every profile deletion appears in the audit trail.
/// </para>
/// </remarks>
public sealed class DeleteCredentialProfileUseCase
{
    private readonly ICredentialProfileRepository _repository;
    private readonly IAuditService _auditService;

    public DeleteCredentialProfileUseCase(
        ICredentialProfileRepository repository,
        IAuditService auditService)
    {
        _repository = repository;
        _auditService = auditService;
    }

    /// <summary>
    /// Soft-deletes a credential profile.
    /// </summary>
    /// <param name="profileId">
    /// The <see cref="DataViewer.Domain.Entities.CredentialProfile.Id"/> to soft-delete.
    /// </param>
    /// <param name="deletedByUserId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the Admin performing the deletion,
    /// extracted from the authenticated JWT access token by the API layer.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the profile has been soft-deleted and the
    /// audit entry has been committed. No value is returned; the API layer should respond
    /// with <c>HTTP 204 No Content</c> on success.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when no non-deleted profile with the specified <paramref name="profileId"/> exists
    /// (including when the profile has already been soft-deleted).
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the DeleteCredentialProfile audit entry cannot be persisted.
    /// The profile is NOT deleted in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task ExecuteAsync(
        Guid profileId,
        Guid deletedByUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Load existing profile to ensure it exists and capture name for audit
        var profile = await _repository.GetByIdAsync(profileId, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            throw new NotFoundException(
                $"Credential profile with ID '{profileId}' not found.");
        }

        // Audit-first: Write DeleteCredentialProfile audit entry BEFORE soft-deleting
        await _auditService.LogCredentialActionAsync(
            deletedByUserId,
            ipAddress,
            AuditActionType.DeleteCredentialProfile,
            profile.Name,
            cancellationToken);

        // Soft-delete the profile (also clears IsActive if profile was active)
        await _repository.SoftDeleteAsync(profileId, cancellationToken);

        // Success — no return value (API responds with HTTP 204)
    }
}
