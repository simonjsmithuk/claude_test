namespace DataViewer.Core.Domain;

/// <summary>Persisted refresh token entry (FR-01, FR-04).</summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>Cryptographically random token value (stored as SHA-256 hash).</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
