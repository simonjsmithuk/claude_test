using System.ComponentModel.DataAnnotations;
using DataViewer.Domain.ValueObjects;

namespace DataViewer.Application.DTOs.UserPreferences;

/// <summary>
/// Represents a user's persisted UI preferences.
/// Used both as the response body for GET /api/preferences and as the
/// request body for PUT /api/preferences.
/// </summary>
public sealed record UserPreferenceDto
{
    /// <summary>
    /// Number of records to display per page in search result listings.
    /// Must be between 1 and <see cref="SearchFilter.MaxPageSize"/> (200).
    /// </summary>
    [Range(1, SearchFilter.MaxPageSize, ErrorMessage = "DefaultPageSize must be between 1 and 200.")]
    public int DefaultPageSize { get; init; } = 25;

    /// <summary>
    /// Default look-back window in calendar days pre-populated in the date-range
    /// filter when the user opens the search view.
    /// Must be a positive value.
    /// </summary>
    [Range(1, 365, ErrorMessage = "DefaultDateRangeDays must be between 1 and 365.")]
    public int DefaultDateRangeDays { get; init; } = 7;

    /// <summary>
    /// The ID of the credential profile pre-selected in the profile picker when
    /// the user opens the search view.
    /// <see langword="null"/> means the system falls back to the active profile.
    /// </summary>
    public Guid? PreferredProfileId { get; init; }
}
