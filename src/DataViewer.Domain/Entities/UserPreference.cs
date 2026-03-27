#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Persisted UI preferences for a single user.
/// </summary>
/// <remarks>
/// This entity has a one-to-one relationship with <see cref="User"/> where
/// <see cref="UserId"/> is simultaneously the primary key and the foreign key.
/// A row is created the first time a user explicitly saves their preferences;
/// until then the <see cref="User.Preference"/> navigation property is <see langword="null"/>
/// and the application falls back to system defaults.
/// </remarks>
public class UserPreference
{
    /// <summary>
    /// Primary key and foreign key — shares the same value as the owning <see cref="User.Id"/>.
    /// Using the user's ID as the PK enforces the one-to-one cardinality at the database level.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Number of records to display per page in search results.
    /// Must be a positive integer; validated at the application layer before persistence.
    /// </summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>
    /// Default look-back window (in calendar days) pre-populated in the date-range filter
    /// when the user opens the search view.
    /// </summary>
    public int DefaultDateRangeDays { get; set; } = 7;

    /// <summary>
    /// The <see cref="CredentialProfile.Id"/> of the profile pre-selected in the
    /// profile picker when the user opens the search view.
    /// <see langword="null"/> when the user has no preferred profile (system default applies).
    /// </summary>
    public Guid? PreferredProfileId { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// The user to whom these preferences belong.
    /// Required (non-null) — preferences cannot exist without an owning user.
    /// </summary>
    public User User { get; set; } = null!;
}
