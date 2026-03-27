#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Singleton row that stores dynamic system-wide configuration values editable by Admins at runtime.
/// </summary>
/// <remarks>
/// The table always contains exactly one row with <see cref="Id"/> = 1.
/// This constraint is enforced by:
/// <list type="bullet">
///   <item><description>Seeding a single row on first migration.</description></item>
///   <item><description>The application layer only ever upserts against <c>Id = 1</c>.</description></item>
/// </list>
/// <para>
/// Using a single typed entity (rather than a key/value bag) provides compile-time safety
/// and eliminates the need for runtime type coercions when reading settings values.
/// </para>
/// </remarks>
public class SystemSettings
{
    /// <summary>
    /// Surrogate primary key, always 1.
    /// Declaring as <see langword="int"/> (not <see langword="Guid"/>) deliberately prevents
    /// accidental insertion of additional rows through standard repository patterns.
    /// </summary>
    // ASSUMPTION: The spec mandates Id = 1 as the singleton sentinel. int is used to make
    //             the "always 1" constraint immediately obvious and prevent accidental
    //             multi-row inserts via Guid.NewGuid() assignment patterns.
    public int Id { get; set; } = 1;

    /// <summary>
    /// Lifetime of a JWT access token, expressed in whole minutes.
    /// Shorter values improve security; longer values reduce re-authentication frequency.
    /// Typical range: 5–60 minutes.
    /// </summary>
    public int JwtAccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Lifetime of a refresh token, expressed in whole hours.
    /// Refresh tokens are stored as hashes and revoked on logout.
    /// Typical range: 1–720 hours (1 hour – 30 days).
    /// </summary>
    public int JwtRefreshTokenHours { get; set; } = 24;

    /// <summary>
    /// Maximum size (in megabytes) of an S3 object body that the API will decompress
    /// and return in a single response. Requests for objects exceeding this cap are
    /// rejected with a 413 status, protecting the server from memory exhaustion.
    /// </summary>
    public int BodySizeCapMb { get; set; } = 10;

    /// <summary>
    /// Number of consecutive failed login attempts after which an account is locked.
    /// A value of 0 disables automatic account lockout entirely.
    /// </summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>UTC timestamp of the most recent administrative update to this row.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
