using DataViewer.Domain.Entities;

namespace DataViewer.Application.Interfaces;

/// <summary>
/// Persistence contract for <see cref="RefreshToken"/> lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Security invariant — raw tokens are never stored:</strong>
/// All methods accept and return only the SHA-256 hex hash of the raw token
/// (see <see cref="ITokenService.GetRefreshTokenHash"/>). The raw token string
/// produced by <see cref="ITokenService.GenerateRefreshToken"/> is returned to
/// the client once at issuance and immediately discarded by the application.
/// No method on this interface ever receives or returns a raw opaque token string.
/// </para>
///
/// <para>
/// <strong>Soft revocation — never delete:</strong>
/// Tokens are revoked by setting <see cref="RefreshToken.IsRevoked"/> to
/// <see langword="true"/> and recording <see cref="RefreshToken.RevokedAt"/>
/// (via <see cref="RefreshToken.Revoke"/>). The row is never physically deleted so
/// that the full token lifecycle is preserved in the audit trail (Product Spec § G-03).
/// </para>
/// </remarks>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Persists a new refresh token record, storing only the SHA-256 hash of the
    /// raw token — never the raw token itself.
    /// </summary>
    /// <remarks>
    /// The caller (application / token service layer) is responsible for:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       Generating the raw token via <see cref="ITokenService.GenerateRefreshToken"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Computing <paramref name="tokenHash"/> via
    ///       <see cref="ITokenService.GetRefreshTokenHash"/> before calling this method.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Delivering the raw token to the client; this method must never receive
    ///       the raw token itself.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// <see cref="RefreshToken.CreatedAt"/> is stamped with the current UTC instant
    /// by the Infrastructure layer, overriding any value set by the caller.
    /// </para>
    /// </remarks>
    /// <param name="userId">The <see cref="User.Id"/> that owns this token.</param>
    /// <param name="tokenHash">
    /// The lowercase SHA-256 hex hash of the raw opaque refresh token.
    /// Must be exactly 64 hex characters (256-bit SHA-256 output).
    /// </param>
    /// <param name="expiresAt">
    /// The UTC timestamp at which this token becomes invalid for exchange.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>The newly created <see cref="RefreshToken"/> entity.</returns>
    Task<RefreshToken> StoreAsync(
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a refresh token by its hash, then atomically marks it as revoked
    /// if valid, returning the entity with its <see cref="RefreshToken.User"/>
    /// navigation property populated.
    /// </summary>
    /// <remarks>
    /// A token is considered valid if and only if:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       A <see cref="RefreshToken"/> row with the given
    ///       <paramref name="tokenHash"/> exists.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="RefreshToken.IsRevoked"/> is <see langword="false"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="RefreshToken.ExpiresAt"/> is strictly greater than
    ///       <see cref="DateTime.UtcNow"/> at the moment of validation.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// When valid, the token is atomically revoked (single database round-trip
    /// combining the validity check with the revocation write) before being
    /// returned, implementing a token-rotation pattern. The caller is responsible
    /// for immediately issuing a new refresh token and access token.
    /// </para>
    ///
    /// <para>
    /// Returns <see langword="null"/> without throwing when the token is unknown,
    /// already revoked, or expired. The caller is responsible for translating a
    /// <see langword="null"/> result into an appropriate error response
    /// (typically <c>HTTP 401</c>).
    /// </para>
    /// </remarks>
    /// <param name="tokenHash">
    /// The lowercase SHA-256 hex hash of the raw refresh token presented by the client.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The revoked <see cref="RefreshToken"/> entity (with
    /// <see cref="RefreshToken.User"/> populated) when the token was valid and has
    /// been successfully revoked, or <see langword="null"/> when the token is
    /// unknown, revoked, or expired.
    /// </returns>
    Task<RefreshToken?> ValidateAndRevokeAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks all active (non-revoked) refresh tokens belonging to the specified
    /// user as revoked, effectively invalidating all concurrent sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by the logout-all-sessions endpoint and by the account-lockout path to
    /// ensure a locked-out user cannot continue using any previously issued refresh
    /// token to obtain new access tokens.
    /// </para>
    ///
    /// <para>
    /// Uses a bulk <c>ExecuteUpdateAsync</c> to revoke all qualifying rows in a
    /// single database round-trip without loading the entities, making the operation
    /// atomic at the database level.
    /// </para>
    ///
    /// <para>
    /// Rows that are already revoked or have already expired are left unchanged —
    /// only active tokens (<c>IsRevoked = false</c> and <c>ExpiresAt &gt; UtcNow</c>)
    /// are updated.
    /// </para>
    ///
    /// <para>
    /// This method is idempotent: calling it when no active tokens exist for the
    /// user succeeds silently (zero rows updated, no exception thrown).
    /// </para>
    /// </remarks>
    /// <param name="userId">
    /// The <see cref="User.Id"/> whose active refresh tokens will be revoked.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>The number of token rows that were revoked.</returns>
    Task<int> RevokeByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
