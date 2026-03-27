namespace DataViewer.Application.UseCases.UserPreferences;

using DataViewer.Application.DTOs.UserPreferences;
using DataViewer.Application.Interfaces;

/// <summary>
/// Retrieves the persisted UI preferences for a single user.
/// </summary>
/// <remarks>
/// <para>
/// <b>Default fallback contract:</b>
/// A <see cref="DataViewer.Domain.Entities.UserPreference"/> row may not exist for a user
/// until they explicitly save their preferences via
/// <see cref="UpdateUserPreferencesUseCase"/>. When no row exists, this use case returns a
/// DTO populated with application-layer default values (page size = 25, date range = 7 days,
/// preferred profile = <see langword="null"/>).
/// </para>
///
/// <para>
/// No audit entry is written for this read-only query. Audit-first (ADR-009) applies only to
/// mutating operations (Create, Update, Delete, View) that change system state or expose
/// sensitive data. Reading one's own UI preferences is informational and non-sensitive.
/// </para>
/// </remarks>
public sealed class GetUserPreferencesUseCase
{
    private readonly IUserPreferencesRepository _repository;

    public GetUserPreferencesUseCase(IUserPreferencesRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Retrieves the UI preferences for the authenticated user.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the authenticated user,
    /// extracted from the JWT access token by the API layer.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="UserPreferenceDto"/> containing either the persisted preferences
    /// (if the user has previously saved them) or application-layer default values.
    /// This method never throws <c>NotFoundException</c> — absence of a row is treated
    /// as "preferences not yet set" rather than an error condition.
    /// </returns>
    public async Task<UserPreferenceDto> ExecuteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Retrieve persisted preferences (may be null if user has never saved preferences)
        var preferences = await _repository.GetByUserIdAsync(userId, cancellationToken);

        // Map to DTO (with default fallback if preferences is null)
        return MapToDto(preferences);
    }

    /// <summary>
    /// Maps a <see cref="DataViewer.Domain.Entities.UserPreference"/> domain entity
    /// (or <see langword="null"/>) to a <see cref="UserPreferenceDto"/>.
    /// </summary>
    /// <remarks>
    /// When <paramref name="preferences"/> is <see langword="null"/> (meaning the user has
    /// never saved their preferences), this method returns a DTO populated with application-layer
    /// default values:
    /// <list type="bullet">
    ///   <item><description>DefaultPageSize = 25</description></item>
    ///   <item><description>DefaultDateRangeDays = 7</description></item>
    ///   <item><description>PreferredProfileId = <see langword="null"/></description></item>
    /// </list>
    /// These defaults are duplicated in the DTO field initialisers and do not require
    /// additional configuration.
    /// </remarks>
    private static UserPreferenceDto MapToDto(DataViewer.Domain.Entities.UserPreference? preferences)
    {
        // If no row exists, return DTO with default values (defined by DTO field initialisers)
        if (preferences is null)
        {
            return new UserPreferenceDto();
        }

        // Map from persisted entity
        return new UserPreferenceDto
        {
            DefaultPageSize = preferences.DefaultPageSize,
            DefaultDateRangeDays = preferences.DefaultDateRangeDays,
            PreferredProfileId = preferences.PreferredProfileId
        };
    }
}
