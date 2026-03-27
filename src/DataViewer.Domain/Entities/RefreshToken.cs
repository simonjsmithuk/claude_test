#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Represents a persisted refresh token issued to a user after successful authentication.
/// Tokens are stored as hashes — the raw token value is never persisted.
/// </summary>
/// <remarks>
/// A user may hold multiple active refresh tokens simultaneously (e.g. multiple devices).
/// Revocation is soft: <see cref="IsRevoked"/> is set to <see langword="true"/> and
/// <see cref="RevokedAt"/> is recorded rather than deleting the row, preserving the audit trail.
/// </remarks>
public class RefreshToken
{
    /// <summary>Primary key — generated on creation, never reassigned.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key referencing the owning <see cref="User"/>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw opaque token string.
    /// The raw token is only held in memory and returned to the client at issuance time.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>UTC timestamp after which this token is no longer valid for exchange.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// <see langword="true"/> when the token has been explicitly invalidated
    /// (logout, rotation, or administrative action).
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>UTC timestamp when this token was first issued.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the token was revoked.
    /// <see langword="null"/> for tokens that have not yet been revoked.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// The user to whom this refresh token belongs.
    /// Required (non-null) — a token cannot exist without an owning user.
    /// </summary>
    public User User { get; set; } = null!;
}
