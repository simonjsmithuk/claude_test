namespace DataViewer.Application.UseCases.AdminSettings;

using DataViewer.Application.DTOs.AdminSettings;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Updates the system-wide configuration settings editable by Admins.
/// </summary>
/// <remarks>
/// <para>
/// <b>Singleton upsert:</b>
/// This use case updates the single <see cref="DataViewer.Domain.Entities.SystemSettings"/>
/// row (Id = 1). The repository's <see cref="ISystemSettingsRepository.UpdateAsync"/>
/// implementation performs an upsert against the well-known primary key to guard against
/// accidental duplicate rows if the seed migration is re-run.
/// </para>
///
/// <para>
/// <b>UpdatedAt server-managed field:</b>
/// The <see cref="DataViewer.Domain.Entities.SystemSettings.UpdatedAt"/> field is populated
/// automatically by the Infrastructure layer's <c>SaveChanges</c> interceptor and must never
/// be sourced from client input. The <see cref="SystemSettingsRequestDto"/> deliberately omits
/// this field to enforce the "server-managed" contract at the type level.
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// An UpdateSystemSettings audit entry is written BEFORE the database update is committed,
/// guaranteeing that every configuration change appears in the audit trail. If the audit write
/// fails, <see cref="AuditFailureException"/> is thrown and the settings are NOT updated.
/// </para>
/// </remarks>
public sealed class UpdateSystemSettingsUseCase
{
    private readonly ISystemSettingsRepository _repository;
    private readonly IAuditService _auditService;

    public UpdateSystemSettingsUseCase(
        ISystemSettingsRepository repository,
        IAuditService auditService)
    {
        _repository = repository;
        _auditService = auditService;
    }

    /// <summary>
    /// Updates the system settings with the supplied values.
    /// </summary>
    /// <param name="request">
    /// The new settings values. All fields must be present — this is a full-replacement
    /// update, not a partial update.
    /// </param>
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
    /// A <see cref="SystemSettingsResponseDto"/> representing the updated settings, including
    /// the server-managed <see cref="SystemSettingsResponseDto.UpdatedAt"/> timestamp.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when the singleton <see cref="DataViewer.Domain.Entities.SystemSettings"/> row
    /// does not exist in the database. This indicates a fatal deployment error (migration not
    /// run or seed data missing) and should never occur in production.
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the UpdateSystemSettings audit entry cannot be persisted.
    /// The settings are NOT updated in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task<SystemSettingsResponseDto> ExecuteAsync(
        SystemSettingsRequestDto request,
        Guid updatedByUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Load existing settings to ensure singleton row exists
        var settings = await _repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            throw new NotFoundException(
                "System settings not found. This is a fatal configuration error — " +
                "the database migration did not seed the singleton SystemSettings row. " +
                "Please run 'dotnet ef database update' to apply migrations.");
        }

        // Apply updates from request DTO (full replacement)
        settings.JwtAccessTokenMinutes = request.JwtAccessTokenMinutes;
        settings.JwtRefreshTokenHours = request.JwtRefreshTokenHours;
        settings.BodySizeCapMb = request.BodySizeCapMb;
        settings.LockoutThreshold = request.LockoutThreshold;
        // UpdatedAt is server-managed — Infrastructure layer will set this automatically

        // Audit-first: Write UpdateSystemSettings audit entry BEFORE persisting changes
        await _auditService.LogAdminActionAsync(
            updatedByUserId,
            ipAddress,
            AuditActionType.UpdateSystemSettings,
            details: BuildAuditDetails(request),
            cancellationToken);

        // Persist the updated settings (upsert against Id = 1)
        await _repository.UpdateAsync(settings, cancellationToken);

        // Reload to get the server-managed UpdatedAt timestamp
        settings = await _repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            // This should never happen (we just wrote the row), but guard against it
            throw new NotFoundException(
                "System settings disappeared after update. This is a fatal database error.");
        }

        // Map to response DTO (includes UpdatedAt)
        return MapToResponseDto(settings);
    }

    /// <summary>
    /// Builds a human-readable audit trail entry describing the settings update.
    /// </summary>
    /// <remarks>
    /// The returned string is stored in <see cref="DataViewer.Domain.Entities.AuditLogEntry.Details"/>
    /// and appears in the audit log viewer. Format is intentionally simple and grep-friendly.
    /// </remarks>
    private static string BuildAuditDetails(SystemSettingsRequestDto request)
    {
        return $"AccessTokenMinutes={request.JwtAccessTokenMinutes}, " +
               $"RefreshTokenHours={request.JwtRefreshTokenHours}, " +
               $"BodySizeCapMb={request.BodySizeCapMb}, " +
               $"LockoutThreshold={request.LockoutThreshold}";
    }

    /// <summary>
    /// Maps a <see cref="DataViewer.Domain.Entities.SystemSettings"/> domain entity
    /// to a <see cref="SystemSettingsResponseDto"/> for API responses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="SystemSettingsResponseDto.UpdatedAt"/> field is mapped from
    /// <see cref="DataViewer.Domain.Entities.SystemSettings.UpdatedAt"/>, which is a
    /// server-managed field populated by the Infrastructure layer's <c>SaveChanges</c>
    /// interceptor.
    /// </para>
    /// <para>
    /// The domain entity uses <see cref="DateTime"/> (Kind = Utc); the DTO uses
    /// <see cref="DateTimeOffset"/> to ensure the serialised JSON includes an explicit
    /// <c>+00:00</c> offset. The conversion is done via
    /// <see cref="DateTime.SpecifyKind(DateTime, DateTimeKind)"/> to preserve UTC semantics.
    /// </para>
    /// </remarks>
    private static SystemSettingsResponseDto MapToResponseDto(
        DataViewer.Domain.Entities.SystemSettings settings)
    {
        return new SystemSettingsResponseDto
        {
            JwtAccessTokenMinutes = settings.JwtAccessTokenMinutes,
            JwtRefreshTokenHours = settings.JwtRefreshTokenHours,
            BodySizeCapMb = settings.BodySizeCapMb,
            LockoutThreshold = settings.LockoutThreshold,
            UpdatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(settings.UpdatedAt, DateTimeKind.Utc),
                TimeSpan.Zero)
        };
    }
}
