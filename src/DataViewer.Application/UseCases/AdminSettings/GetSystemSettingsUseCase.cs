namespace DataViewer.Application.UseCases.AdminSettings;

using DataViewer.Application.DTOs.AdminSettings;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Retrieves the current system-wide configuration settings editable by Admins.
/// </summary>
/// <remarks>
/// <para>
/// <b>Singleton contract:</b>
/// The <see cref="DataViewer.Domain.Entities.SystemSettings"/> table always contains exactly
/// one row with <c>Id = 1</c>, seeded by the EF Core migration. This use case retrieves that
/// singleton row and maps it to a <see cref="SystemSettingsResponseDto"/>.
/// </para>
///
/// <para>
/// <b>Fatal configuration error handling:</b>
/// If <see cref="ISystemSettingsRepository.GetAsync"/> returns <see langword="null"/>
/// (which should never occur on a correctly seeded database), this use case throws
/// <see cref="NotFoundException"/> rather than returning default values. This forces an
/// immediate deployment diagnosis rather than silently using in-memory fallbacks that
/// might diverge from actual persisted values.
/// </para>
///
/// <para>
/// <b>No audit entry:</b>
/// Reading system settings is not audited under the current design. Audit-first (ADR-009)
/// applies to mutating operations (Create, Update, Delete) and sensitive data access (View).
/// Reading configuration is informational and does not expose transaction data or change
/// system state.
/// </para>
/// </remarks>
public sealed class GetSystemSettingsUseCase
{
    private readonly ISystemSettingsRepository _repository;

    public GetSystemSettingsUseCase(ISystemSettingsRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Retrieves the current system settings.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="SystemSettingsResponseDto"/> containing the current system settings,
    /// including the server-managed <see cref="SystemSettingsResponseDto.UpdatedAt"/>
    /// timestamp.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when the singleton <see cref="DataViewer.Domain.Entities.SystemSettings"/> row
    /// does not exist in the database. This indicates a fatal deployment error (migration not
    /// run or seed data missing) and should never occur in production.
    /// </exception>
    public async Task<SystemSettingsResponseDto> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        // Retrieve the singleton settings row
        var settings = await _repository.GetAsync(cancellationToken);

        // Fatal configuration error: singleton row not seeded
        if (settings is null)
        {
            throw new NotFoundException(
                "SystemSettings",
                "1");
        }

        // Map to response DTO (includes UpdatedAt timestamp)
        return MapToResponseDto(settings);
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
