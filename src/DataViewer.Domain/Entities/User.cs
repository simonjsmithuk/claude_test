#nullable enable

namespace DataViewer.Domain.Entities;

using DataViewer.Domain.Enums;

/// <summary>
/// Represents an application user account.
/// </summary>
/// <remarks>
/// Holds authentication credentials, role assignment, and account-lockout state.
/// Passwords are stored exclusively as bcrypt hashes — the plain-text value is
/// never persisted or logged. Lockout is triggered automatically when
/// <see cref="FailedLoginCount"/> reaches the system-configured threshold; it may
/// also be cleared manually by an Admin by resetting <see cref="IsLocked"/> and
/// <see cref="LockoutUntil"/> through the administration API.
///
/// <para>
/// Valid lockout state combinations:
/// <list type="table">
///   <listheader><term>IsLocked</term><term>LockoutUntil</term><term>Meaning</term></listheader>
///   <item><term>false</term><term>null</term><term>Active account (normal state)</term></item>
///   <item><term>true</term><term>non-null</term><term>Time-limited automatic lockout</term></item>
///   <item><term>true</term><term>null</term><term>Permanent administrative lock (no expiry)</term></item>
///   <item><term>false</term><term>non-null</term><term>INVALID — stale data; treat as unlocked</term></item>
/// </list>
/// Use <see cref="IsEffectivelyLocked"/> to evaluate the combined state correctly
/// rather than reading <see cref="IsLocked"/> in isolation.
/// </para>
/// </remarks>
public class User
{
    /// <summary>
    /// Primary key. Generated once on entity construction; never reassigned.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique login name chosen at registration.
    /// Case-insensitive comparison is enforced at the application layer.
    /// The database index that backs the uniqueness constraint should use a
    /// case-insensitive collation so that enforcement is consistent across
    /// MySQL and PostgreSQL.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Unique email address used as an alternative login identifier and for
    /// account-related notifications.
    /// Stored in lower-case normal form; normalisation is applied before persistence.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// BCrypt password hash — never exposed in any API response or log output.
    /// The application layer must use a work factor of ≥ 12 when computing the hash.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Access level granted to this user.
    /// The underlying integer value is persisted to the database;
    /// enum members must not be reordered or renumbered.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Viewer;

    /// <summary>
    /// <see langword="true"/> when the account is locked due to repeated failed logins
    /// or a manual administrative lock.
    /// Always evaluate combined lock state via <see cref="IsEffectivelyLocked"/> rather
    /// than reading this property in isolation; see the class remarks for the full
    /// state-machine invariant table.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// UTC timestamp at which an automatic lockout expires.
    /// <see langword="null"/> when the account is not locked, or when the lock is
    /// permanent (administratively imposed with no expiry).
    /// </summary>
    public DateTime? LockoutUntil { get; set; }

    /// <summary>
    /// Number of consecutive failed login attempts since the last successful
    /// authentication. Reset to zero on every successful login.
    /// When this value reaches the <c>SystemSettings.LockoutThreshold</c>, the
    /// account is locked and <see cref="IsLocked"/> is set to <see langword="true"/>.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// UTC timestamp when the user account was first created.
    /// Initialised to <see langword="default"/> here; the Infrastructure layer
    /// (EF Core <c>SaveChanges</c> interceptor or database <c>DEFAULT CURRENT_TIMESTAMP</c>)
    /// is the authoritative writer so the persisted value reflects the actual
    /// database write time rather than the in-memory object construction time.
    /// </summary>
    public DateTime CreatedAt { get; set; } = default;

    /// <summary>
    /// UTC timestamp of the most recent successful login.
    /// <see langword="null"/> before the user has authenticated for the first time.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// All refresh tokens ever issued to this user — both active and revoked.
    /// Tokens are retained after revocation to preserve the audit trail.
    /// </summary>
    /// <remarks>
    /// This collection is unbounded and includes revoked tokens. Infrastructure
    /// queries MUST NOT eagerly load this collection via <c>Include()</c>; always
    /// query <see cref="RefreshToken"/> directly with an active/non-expired predicate.
    /// See ADR-003 for the token retention policy.
    /// </remarks>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>
    /// Persisted UI preferences for this user.
    /// One-to-one relationship; <see langword="null"/> until the user explicitly
    /// saves preferences. The application falls back to application-layer constants
    /// for page-size and date-range defaults when this property is <see langword="null"/>.
    /// </summary>
    public UserPreference? Preference { get; set; }

    // ── Domain methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the combined two-flag lockout state correctly, accounting for
    /// expired time-limited lockouts.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC instant, supplied by the caller so that this method remains
    /// pure and testable without a hidden dependency on <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <see cref="IsLocked"/> is set AND either the
    /// lockout has no expiry (<see cref="LockoutUntil"/> is <see langword="null"/>)
    /// or the lockout has not yet expired at <paramref name="utcNow"/>.
    /// <see langword="false"/> in all other cases, including the invalid
    /// <c>IsLocked=false, LockoutUntil=non-null</c> stale-data state.
    /// </returns>
    public bool IsEffectivelyLocked(DateTime utcNow) =>
        IsLocked && (LockoutUntil is null || LockoutUntil > utcNow);
}
