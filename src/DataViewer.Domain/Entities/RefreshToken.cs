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
/// The raw token MUST be produced by <c>RandomNumberGenerator.GetBytes(32)</c> (256 bits
/// of CSPRNG output) before hashing. Lower-entropy raw tokens make an unsalted SHA-256
/// hash brute-forceable; the 256-bit CSPRNG value itself acts as the effective salt.
/// </para>
///
/// <para>
/// A single user may hold multiple active refresh tokens simultaneously, allowing
/// concurrent sessions from different browsers or devices.
/// </para>
///
/// <para>
/// Revocation is soft: call <see cref="Revoke"/> which sets <see cref="IsRevoked"/> to
/// <see langword="true"/> and captures <see cref="RevokedAt"/> atomically, rather than
/// deleting the row. This preserves the full token lifecycle in the audit trail and
/// prevents token-replay attacks that target a gap between deletion and expiry.
/// Never set <see cref="IsRevoked"/> or <see cref="RevokedAt"/> directly — use
/// <see cref="Revoke"/> to keep the two flags in a consistent state.
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
    /// The raw token MUST originate from <c>RandomNumberGenerator.GetBytes(32)</c>
    /// (256 bits of CSPRNG output) to ensure sufficient entropy for unsalted SHA-256.
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
    /// Always set via <see cref="Revoke"/>; never set directly.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// UTC timestamp when this token was first issued.
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
    /// UTC timestamp when the token was revoked.
    /// <see langword="null"/> for tokens that have not yet been revoked.
    /// Always set in concert with <see cref="IsRevoked"/> via <see cref="Revoke"/> —
    /// never set independently.
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

    // ── Domain methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Revokes this token by atomically setting both <see cref="IsRevoked"/> and
    /// <see cref="RevokedAt"/> in a single operation.
    /// </summary>
    /// <param name="revokedAt">
    /// The UTC timestamp of revocation. Supplied by the caller to keep this method
    /// pure and testable without a hidden dependency on <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <remarks>
    /// Always use this method instead of setting <see cref="IsRevoked"/> or
    /// <see cref="RevokedAt"/> directly — this ensures the two flags are never left
    /// in an inconsistent state (e.g. <c>IsRevoked=true</c> with a null <c>RevokedAt</c>).
    /// </remarks>
    public void Revoke(DateTime revokedAt)
    {
        IsRevoked = true;
        RevokedAt = revokedAt;
    }
}
