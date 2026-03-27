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
/// also be cleared manually by an Admin by calling <see cref="Unlock"/> through
/// the administration API.
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
///
/// <para>
/// Mutate lockout state exclusively through <see cref="Lock"/>, <see cref="Unlock"/>,
/// <see cref="RecordFailedLogin"/>, and <see cref="RecordSuccessfulLogin"/> to preserve
/// the state-machine invariants above. Direct property assignment bypasses these
/// guards and can produce inconsistent two-flag state.
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
    /// Always mutate via <see cref="Lock"/> or <see cref="Unlock"/>; never set directly.
    /// </summary>
    public DateTime? LockoutUntil { get; set; }

    /// <summary>
    /// Number of consecutive failed login attempts since the last successful
    /// authentication. Reset to zero on every successful login.
    /// When this value reaches the <c>SystemSettings.LockoutThreshold</c>, the
    /// account is locked and <see cref="IsLocked"/> is set to <see langword="true"/>.
    /// Mutate via <see cref="RecordFailedLogin"/> and <see cref="RecordSuccessfulLogin"/>.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// UTC timestamp when the user account was first created.
    /// Initialised to <see langword="default"/> here; the Infrastructure layer
    /// (EF Core <c>SaveChanges</c> interceptor or database <c>DEFAULT CURRENT_TIMESTAMP</c>)
    /// is the authoritative writer so the persisted value reflects the actual
    /// database write time rather than the in-memory object construction time.
    /// </summary>
    /// <remarks>
    /// ⚠️ Risk: if the Infrastructure interceptor is missed, this field persists as
    /// <c>DateTime.MinValue</c> (0001-01-01). Monitor this via integration tests.
    /// </remarks>
    public DateTime CreatedAt { get; set; } = default;

    /// <summary>
    /// UTC timestamp of the most recent successful login.
    /// <see langword="null"/> before the user has authenticated for the first time.
    /// Mutate via <see cref="RecordSuccessfulLogin"/>.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// All refresh tokens ever issued to this user — both active and revoked.
    /// Tokens are retained after revocation to preserve the audit trail.
    /// </summary>
    /// <remarks>
    /// ⚠️ Performance: This collection is unbounded and includes revoked tokens.
    /// Infrastructure queries MUST NOT eagerly load this collection via <c>Include()</c>;
    /// always query <see cref="RefreshToken"/> directly with an active/non-expired
    /// predicate. The protected setter prevents external code from replacing the
    /// collection, but EF Core can still populate it during materialisation. If
    /// lazy-loading proxies are ever enabled, this collection will be hydrated on
    /// every <see cref="User"/> access — ensure <c>AutoInclude(false)</c> is set
    /// in the DbContext Fluent API for this navigation. See ADR-003 for the token
    /// retention policy.
    /// </remarks>
    public ICollection<RefreshToken> RefreshTokens { get; protected set; } = [];

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

    /// <summary>
    /// Applies a lockout to the account, atomically setting both
    /// <see cref="IsLocked"/> and <see cref="LockoutUntil"/> in one operation.
    /// </summary>
    /// <param name="lockoutUntil">
    /// The UTC expiry of the lockout. Pass <see langword="null"/> for a permanent
    /// administrative lock with no expiry.
    /// </param>
    /// <remarks>
    /// Always use this method instead of setting <see cref="IsLocked"/> directly —
    /// this ensures the two flags are never left in an inconsistent state.
    /// </remarks>
    public void Lock(DateTime? lockoutUntil)
    {
        IsLocked = true;
        LockoutUntil = lockoutUntil;
    }

    /// <summary>
    /// Clears the lockout state, atomically resetting <see cref="IsLocked"/>,
    /// <see cref="LockoutUntil"/>, and <see cref="FailedLoginCount"/> in one operation.
    /// </summary>
    /// <remarks>
    /// Always use this method instead of setting <see cref="IsLocked"/> directly —
    /// this ensures all three interdependent fields are reset together.
    /// </remarks>
    public void Unlock()
    {
        IsLocked = false;
        LockoutUntil = null;
        FailedLoginCount = 0;
    }

    /// <summary>
    /// Increments <see cref="FailedLoginCount"/> by one to track a single failed
    /// authentication attempt.
    /// </summary>
    /// <remarks>
    /// The caller (application layer) is responsible for comparing the updated
    /// count against <c>SystemSettings.LockoutThreshold</c> and then calling
    /// <see cref="Lock"/> when the threshold is reached.
    /// </remarks>
    public void RecordFailedLogin()
    {
        FailedLoginCount++;
    }

    /// <summary>
    /// Records a successful authentication by resetting <see cref="FailedLoginCount"/>
    /// and updating <see cref="LastLoginAt"/>.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC instant to record as the last login time.
    /// Supplied by the caller to keep this method pure and testable.
    /// </param>
    public void RecordSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginCount = 0;
        LastLoginAt = utcNow;
    }
}
