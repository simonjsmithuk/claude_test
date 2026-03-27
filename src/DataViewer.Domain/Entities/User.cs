#nullable enable

namespace DataViewer.Domain.Entities;

using DataViewer.Domain.Enums;

/// <summary>
/// Represents an application user account.
/// Holds authentication state, role assignment, and lockout tracking.
/// </summary>
public class User
{
    /// <summary>Primary key — generated on creation, never reassigned.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique login name chosen at registration.
    /// Case-insensitive comparison is enforced at the application layer.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Unique email address. Used as an alternative login identifier
    /// and for account-related notifications.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// bcrypt password hash — never exposed in any API response or log output.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Access level granted to this user.
    /// Integer value is persisted; do not reorder enum members.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Viewer;

    /// <summary>
    /// <see langword="true"/> when the account is locked due to repeated failed logins.
    /// Resolved automatically once <see cref="LockoutUntil"/> passes, or manually by an Admin.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// UTC timestamp at which the automatic lockout expires.
    /// <see langword="null"/> when the account is not locked.
    /// </summary>
    public DateTime? LockoutUntil { get; set; }

    /// <summary>
    /// Count of consecutive failed login attempts since the last successful login.
    /// Reset to zero on successful authentication.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>UTC timestamp when the user account was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent successful login.
    /// <see langword="null"/> before the first successful login.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// All refresh tokens ever issued for this user (both active and revoked).
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// <summary>
    /// Persisted UI preferences for this user.
    /// One-to-one relationship; <see langword="null"/> until the user saves preferences for the first time.
    /// </summary>
    public UserPreference? Preference { get; set; }
}
