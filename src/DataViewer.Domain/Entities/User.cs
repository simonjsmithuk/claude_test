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
    /// The application layer resolves automatic lockout expiry by comparing
    /// <see cref="LockoutUntil"/> with the current UTC time at login time.
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

    /// <summary>UTC timestamp when the user account was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

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
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// <summary>
    /// Persisted UI preferences for this user.
    /// One-to-one relationship; <see langword="null"/> until the user explicitly
    /// saves preferences. The application falls back to system defaults when
    /// this property is <see langword="null"/>.
    /// </summary>
    public UserPreference? Preference { get; set; }
}
