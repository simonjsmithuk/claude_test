#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Represents a persisted refresh token issued to a user after successful authentication.
/// </summary>
/// <remarks>
/// The raw opaque token string is generated in memory, returned to the client once,
/// and never stored. Only a SHA-256 hash of the token is persisted here so that a
/// database breach does not expose reusable credentials.
///
/// <para>
/// A single user may hold multiple active refresh tokens simultaneously, allowing
/// concurrent sessions from different browsers or devices.
/// </para>
///
/// <para>
/// Revocation is soft: <see cref="IsRevoked"/> is set to <see langword="true"/> and
/// <see cref="RevokedAt"/> is captured rather than deleting the row. This preserves
/// the full token lifecycle in the audit trail and prevents token-replay attacks
/// that target a gap between deletion and expiry.
/// </para>
/// </remarks>
public class RefreshToken
{
    /// <summary>
    /// Primary key. Generated once on entity construction; never reassigned.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key referencing the owning <see cref="User"/>.
    /// Every refresh token belongs to exactly one user.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw opaque token string.
    /// Computed with <c>SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))</c>
    /// and stored as a lower-case hex string.
    /// The raw token is held only in memory and returned to the client at issuance.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp after which this token is no longer valid for exchange.
    /// The exchange endpoint must reject tokens where <c>ExpiresAt &lt; DateTime.UtcNow</c>
    /// even if <see cref="IsRevoked"/> is <see langword="false"/>.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// <see langword="true"/> when the token has been explicitly invalidated through
    /// logout, token rotation, or an administrative action.
    /// A revoked token must not be accepted for access-token exchange.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>UTC timestamp when this token was first issued.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the token was revoked.
    /// <see langword="null"/> for tokens that have not yet been revoked.
    /// Always set in concert with <see cref="IsRevoked"/> — never set independently.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>
    /// The user to whom this refresh token belongs.
    /// Required (non-null) — a refresh token cannot exist without an owning user.
    /// Initialised to <c>null!</c> to satisfy the nullable-reference-type compiler;
    /// EF Core always populates this when the entity is loaded with its principal.
    /// </summary>
    public User User { get; set; } = null!;
}
