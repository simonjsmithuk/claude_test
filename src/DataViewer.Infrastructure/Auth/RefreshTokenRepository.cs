using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Exceptions;
using DataViewer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Auth;

/// <summary>
/// Entity Framework Core implementation of <see cref="IRefreshTokenRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Security invariant — only hashes are persisted:</strong>
/// Every method receives a SHA-256 hex hash of the raw token, never the raw token
/// itself. The hash is computed by the caller via
/// <see cref="ITokenService.GetRefreshTokenHash"/> before any method on this
/// repository is invoked. If the raw token were stored and the database were
/// compromised, attackers could obtain valid refresh tokens; storing only the hash
/// limits the blast radius to a token-hash table that cannot be used directly.
/// </para>
///
/// <para>
/// <strong>Atomic validate-and-revoke pattern:</strong>
/// <see cref="ValidateAndRevokeAsync"/> uses a single <c>ExecuteUpdateAsync</c>
/// with a compound WHERE predicate (hash match AND not revoked AND not expired)
/// to implement token rotation atomically. The update either affects exactly one
/// row (valid, now revoked) or zero rows (invalid/expired/already-revoked).
/// This eliminates the TOCTOU (time-of-check / time-of-use) race condition that
/// a load-then-check-then-save pattern would introduce under concurrent requests.
/// </para>
///
/// <para>
/// <strong>Bulk revocation via <c>ExecuteUpdateAsync</c>:</strong>
/// <see cref="RevokeByUserIdAsync"/> revokes all active tokens for a user in a
/// single parameterised UPDATE without loading any entities. This is the correct
/// pattern for logout-all-sessions operations where the exact token count is unknown
/// and loading every row would be wasteful.
/// </para>
///
/// <para>
/// <strong>No raw SQL:</strong> All queries and mutations are expressed exclusively
/// via EF Core LINQ operators and the <c>ExecuteUpdateAsync</c> bulk API.
/// </para>
/// </remarks>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<RefreshTokenRepository> _logger;

    /// <summary>
    /// Initialises the repository with the scoped <see cref="AppDbContext"/>
    /// and a logger for diagnostic output.
    /// </summary>
    /// <param name="context">
    /// The EF Core database context for this unit of work (HTTP request scope).
    /// </param>
    /// <param name="logger">
    /// Structured logger for Store / Validate-and-Revoke / Bulk-Revoke operations.
    /// </param>
    public RefreshTokenRepository(
        AppDbContext context,
        ILogger<RefreshTokenRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ── IRefreshTokenRepository implementation ────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <see cref="RefreshToken.CreatedAt"/> is set to <see cref="DateTime.UtcNow"/>
    /// here; the <see cref="AppDbContext.EnforceUtcDateTimes"/> interceptor will
    /// also normalise the <see cref="DateTimeKind"/> for Npgsql compatibility.
    /// </para>
    ///
    /// <para>
    /// A new <see cref="Guid"/> primary key is assigned by the entity's own initialiser
    /// (<c>Id = Guid.NewGuid()</c> on <see cref="RefreshToken"/>), so this method
    /// does not set it explicitly.
    /// </para>
    /// </remarks>
    public async Task<RefreshToken> StoreAsync(
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash, nameof(tokenHash));

        var refreshToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IsRevoked = false,
            // CreatedAt is set here; AppDbContext.EnforceUtcDateTimes normalises the kind.
            CreatedAt = DateTime.UtcNow,
        };

        _logger.LogDebug(
            "Storing refresh token for user {UserId} (expires {ExpiresAt:O})",
            userId,
            expiresAt);

        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Refresh token stored for user {UserId} (Id={TokenId})",
            userId,
            refreshToken.Id);

        return refreshToken;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The validate-and-revoke operation is implemented as a single
    /// <c>ExecuteUpdateAsync</c> with a compound WHERE predicate:
    /// <code>
    /// UPDATE RefreshTokens
    ///   SET IsRevoked = true, RevokedAt = @utcNow
    /// WHERE TokenHash = @hash
    ///   AND IsRevoked = false
    ///   AND ExpiresAt > @utcNow
    /// </code>
    /// If the update affects exactly one row the token was valid; zero rows means
    /// the token was unknown, already revoked, or expired.
    /// </para>
    ///
    /// <para>
    /// After a successful revocation the entity is re-loaded with its
    /// <see cref="RefreshToken.User"/> navigation property populated so the caller
    /// can proceed directly to access-token issuance without a second database hit.
    /// </para>
    /// </remarks>
    public async Task<RefreshToken?> ValidateAndRevokeAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash, nameof(tokenHash));

        var utcNow = DateTime.UtcNow;

        _logger.LogDebug(
            "Attempting validate-and-revoke for token hash (prefix={HashPrefix}...)",
            tokenHash[..Math.Min(8, tokenHash.Length)]);

        // Single atomic UPDATE: the compound WHERE ensures only a valid
        // (non-revoked, non-expired) token with the given hash is matched.
        // If the token was already revoked or has expired, affected == 0.
        var affected = await _context.RefreshTokens
            .Where(rt =>
                rt.TokenHash == tokenHash
                && !rt.IsRevoked
                && rt.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(rt => rt.IsRevoked, true)
                    .SetProperty(rt => rt.RevokedAt, utcNow),
                cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning(
                "Refresh token validation failed: token not found, already revoked, or expired "
                + "(hash prefix={HashPrefix}...)",
                tokenHash[..Math.Min(8, tokenHash.Length)]);

            return null;
        }

        // Re-load the now-revoked entity including the User navigation property
        // so the caller can issue a new access token immediately.
        var token = await _context.RefreshTokens
            .Include(rt => rt.User)
            .AsNoTracking()
            .SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            // This branch is theoretically unreachable because we just updated the row,
            // but we guard against it defensively (e.g. cascading delete race).
            _logger.LogError(
                "ValidateAndRevokeAsync: token was revoked (affected={Affected}) "
                + "but could not be re-loaded (hash prefix={HashPrefix}...)",
                affected,
                tokenHash[..Math.Min(8, tokenHash.Length)]);

            return null;
        }

        _logger.LogInformation(
            "Refresh token validated and revoked for user {UserId} (Id={TokenId})",
            token.UserId,
            token.Id);

        return token;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Issues a single bulk <c>UPDATE</c> targeting all active tokens for the given
    /// user — tokens where <c>IsRevoked = false</c> and <c>ExpiresAt &gt; UtcNow</c>.
    /// Already-revoked or already-expired tokens are left untouched. The operation is
    /// idempotent: calling it when no active tokens exist for the user succeeds silently.
    /// </remarks>
    public async Task<int> RevokeByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Revoking all active refresh tokens for user {UserId}", userId);

        var utcNow = DateTime.UtcNow;

        var affected = await _context.RefreshTokens
            .Where(rt =>
                rt.UserId == userId
                && !rt.IsRevoked
                && rt.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(rt => rt.IsRevoked, true)
                    .SetProperty(rt => rt.RevokedAt, utcNow),
                cancellationToken);

        _logger.LogInformation(
            "Revoked {Count} active refresh token(s) for user {UserId}",
            affected,
            userId);

        return affected;
    }
}
