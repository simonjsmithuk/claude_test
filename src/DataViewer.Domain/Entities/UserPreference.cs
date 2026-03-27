#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Persisted UI preferences for a single user, controlling default search behaviour
/// and which credential profile is pre-selected in the search view.
/// </summary>
/// <remarks>
/// This entity participates in a one-to-one relationship with <see cref="User"/> where
/// <see cref="UserId"/> is simultaneously the primary key and the foreign key.
/// Using the user's own ID as the primary key enforces the one-to-one cardinality at
/// the database level with no additional unique index required.
///
/// <para>
/// A row is created the first time a user explicitly saves their preferences through
/// the API. Until then, <see cref="User.Preference"/> is <see langword="null"/> and the
/// application falls back to system-wide defaults
/// (<see cref="SystemSettings.JwtAccessTokenMinutes"/> et al. for token settings;
/// page-size and date-range defaults are defined in application-layer constants).
/// </para>
/// </remarks>
public class UserPreference
{
    /// <summary>
    /// Primary key and foreign key to <see cref="User"/>.
    /// Shares the same <see cref="Guid"/> value as the owning <see cref="User.Id"/>.
    /// EF Core is configured to treat this as both the PK and the FK for the
    /// one-to-one relationship (shared primary key pattern).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Number of records to display per page in search result listings.
    /// Must be a positive integer in the range [1, 200]; validated at the
    /// application layer before persistence.
    /// Default: 25 records per page.
    /// </summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>
    /// Default look-back window in calendar days pre-populated in the date-range
    /// filter when the user opens the search view.
    /// Default: 7 days (last week).
    /// </summary>
    public int DefaultDateRangeDays { get; set; } = 7;

    /// <summary>
    /// The <see cref="CredentialProfile.Id"/> of the profile pre-selected in the
    /// profile picker when the user opens the search view.
    /// <see langword="null"/> when the user has set no preference — the application
    /// falls back to the system-active profile (<see cref="CredentialProfile.IsActive"/>).
    /// </summary>
    public Guid? PreferredProfileId { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// The user to whom these preferences belong.
    /// Required (non-null) — a preference row cannot exist without an owning user.
    /// Initialised to <c>null!</c> to satisfy the nullable-reference-type compiler;
    /// EF Core always populates this property when the entity is loaded with its principal.
    /// </summary>
    public User User { get; set; } = null!;
}
