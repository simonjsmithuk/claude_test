#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Singleton row that stores dynamic system-wide configuration values editable
/// by Admins at runtime without requiring an application restart.
/// </summary>
/// <remarks>
/// The table always contains exactly one row with <see cref="Id"/> = 1.
/// This invariant is enforced by two complementary mechanisms:
/// <list type="bullet">
///   <item>
///     <description>
///       The EF Core migration seeds a single row with <c>Id = 1</c> and
///       default values on first deployment.
///     </description>
///   </item>
///   <item>
///     <description>
///       The application layer exclusively performs upsert operations against
///       <c>Id = 1</c>, making it impossible to insert a second row through
///       normal application code paths.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="Id"/> has a private setter, preventing any caller from
///       constructing a <see cref="SystemSettings"/> with a different key value.
///       EF Core can still set private properties via reflection during materialisation.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Using a single, strongly-typed entity (rather than a generic key/value bag)
/// provides compile-time safety for settings access and eliminates runtime
/// type-coercion errors when reading numeric settings.
/// </para>
/// </remarks>
public class SystemSettings
{
    // ASSUMPTION: The spec mandates Id = 1 as the singleton sentinel value.
    //             int (not Guid) is intentionally used here: Guid.NewGuid() patterns
    //             used elsewhere in the domain would accidentally allow insertion of
    //             additional rows, breaking the singleton contract. An int default of 1
    //             makes the constraint explicit and self-documenting.

    /// <summary>
    /// Surrogate primary key, always 1.
    /// The <see langword="int"/> type (rather than <see langword="Guid"/>) is deliberate:
    /// it prevents accidental multi-row inserts through standard repository patterns
    /// that rely on <c>Guid.NewGuid()</c> assignment.
    /// The private setter enforces the singleton invariant at the type level while
    /// remaining settable by EF Core via reflection during entity materialisation.
    /// </summary>
    public int Id { get; private set; } = 1;

    /// <summary>
    /// Lifetime of a JWT access token expressed in whole minutes.
    /// Shorter values improve security at the cost of more frequent token refreshes.
    /// Recommended range: 5–60 minutes.
    /// Default: 15 minutes.
    /// </summary>
    public int JwtAccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Lifetime of a refresh token expressed in whole hours.
    /// Refresh tokens are stored as hashes and revoked on explicit logout or rotation.
    /// Recommended range: 1–720 hours (1 hour – 30 days).
    /// Default: 24 hours.
    /// </summary>
    public int JwtRefreshTokenHours { get; set; } = 24;

    /// <summary>
    /// Maximum size in megabytes of an S3 object body that the API will decompress
    /// and return in a single response.
    /// Requests for objects that exceed this cap are rejected with HTTP 413 to
    /// protect the server from excessive memory allocation on large objects.
    /// Default: 10 MB.
    /// </summary>
    public int BodySizeCapMb { get; set; } = 10;

    /// <summary>
    /// Number of consecutive failed login attempts after which a user account is
    /// automatically locked and <see cref="User.IsLocked"/> is set to
    /// <see langword="true"/>.
    /// A value of <c>0</c> disables automatic account lockout entirely.
    /// Default: 5 attempts.
    /// </summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>
    /// UTC timestamp of the most recent administrative update to this singleton row.
    /// Initialised to <see langword="default"/> here; the Infrastructure layer
    /// (EF Core <c>SaveChanges</c> interceptor) is the authoritative writer so the
    /// persisted value reflects the actual database write time rather than the
    /// in-memory object construction time.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = default;
}
