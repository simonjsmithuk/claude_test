namespace DataViewer.Core.Domain;

/// <summary>Application user account.</summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>bcrypt hash — never stored or logged in plaintext (NFR-10).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = Roles.Viewer;
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public UserPreference? Preference { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
}

/// <summary>Application role constants (NFR-06).</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";
}
