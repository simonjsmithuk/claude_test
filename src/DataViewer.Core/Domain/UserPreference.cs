namespace DataViewer.Core.Domain;

/// <summary>
/// Per-user UI preferences persisted to the database (FR-27, FR-28, US-18).
/// </summary>
public class UserPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>Default number of results per page (FR-15).</summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>Default look-back window in hours for the date-range filter.</summary>
    public int DefaultDateRangeHours { get; set; } = 24;

    /// <summary>Preferred S3 credential profile ID (nullable — no preference set).</summary>
    public int? PreferredProfileId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public S3CredentialProfile? PreferredProfile { get; set; }
}
