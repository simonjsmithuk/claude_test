namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;

/// <summary>
/// Generates and validates JWT access tokens and opaque refresh tokens
/// (Product Spec § FR-01 Authentication, § FR-03 Token Refresh).
/// </summary>
/// <remarks>
/// Access tokens are short-lived signed JWTs. Refresh tokens are opaque
/// cryptographically random strings whose SHA-256 hash is persisted in the
/// <see cref="RefreshToken"/> table — the raw token is returned to the client
/// once at issuance and never stored.
///
/// <para>
/// This interface supersedes the <c>DataViewer.Core.Interfaces.ITokenService</c>
/// declared in the legacy Core project. All new application-layer use cases must
/// depend on this interface, which lives in the canonical Application layer.
/// </para>
///
/// <para>
/// Token lifetime values are read from <see cref="SystemSettings"/> at generation
/// time so that Admin-configured changes take effect without an application restart.
/// </para>
/// </remarks>
public interface ITokenService
{
    /// <summary>
    /// Creates a signed JWT access token for the given authenticated user.
    /// </summary>
    /// <remarks>
    /// The token claims must include at minimum: <c>sub</c> (user ID as a string),
    /// <c>name</c> (username), <c>role</c> (user role), <c>jti</c> (unique token ID),
    /// and <c>exp</c> (expiry derived from <see cref="SystemSettings.JwtAccessTokenMinutes"/>).
    /// </remarks>
    /// <param name="user">
    /// The authenticated <see cref="User"/> for whom the token is being issued.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <returns>A compact serialised (Base64url-encoded) JWT string.</returns>
    string GenerateAccessToken(User user);

    /// <summary>
    /// Generates a cryptographically random opaque refresh token string.
    /// </summary>
    /// <remarks>
    /// The raw token is produced by <c>RandomNumberGenerator.GetBytes(32)</c>
    /// (256 bits of CSPRNG output) and Base-64 encoded for transport. The raw token
    /// is returned to the caller for delivery to the client and must never be stored.
    /// Use <see cref="GetRefreshTokenHash"/> to derive the hash for persistence.
    /// </remarks>
    /// <returns>
    /// A cryptographically random Base-64 encoded string suitable for use as an
    /// opaque refresh token.
    /// </returns>
    string GenerateRefreshToken();

    /// <summary>
    /// Computes the SHA-256 hex hash of a raw refresh token string for storage in
    /// <see cref="RefreshToken.TokenHash"/>.
    /// </summary>
    /// <remarks>
    /// Call this method immediately after <see cref="GenerateRefreshToken"/> to obtain
    /// the hash to persist. The raw token itself must be discarded after being returned
    /// to the client — only this hash is stored.
    /// </remarks>
    /// <param name="token">
    /// The raw opaque refresh token string produced by <see cref="GenerateRefreshToken"/>.
    /// </param>
    /// <returns>
    /// A lowercase hexadecimal SHA-256 hash string suitable for storage in
    /// <see cref="RefreshToken.TokenHash"/>.
    /// </returns>
    string GetRefreshTokenHash(string token);

    /// <summary>
    /// Validates a refresh token hash against the backing store, confirming it is
    /// non-expired and non-revoked.
    /// </summary>
    /// <remarks>
    /// Implementations must check both <see cref="RefreshToken.ExpiresAt"/> and
    /// <see cref="RefreshToken.IsRevoked"/> to determine validity. A token that is
    /// expired but not revoked (or vice versa) must still be treated as invalid.
    /// The implementation should also load the associated <see cref="User"/> so the
    /// caller can proceed with access-token issuance without a second database round-trip.
    /// </remarks>
    /// <param name="tokenHash">
    /// The SHA-256 hex hash of the raw refresh token presented by the client,
    /// obtained by calling <see cref="GetRefreshTokenHash"/> on the received value.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The <see cref="RefreshToken"/> entity (with its <see cref="RefreshToken.User"/>
    /// navigation property populated) when the token is valid, or
    /// <see langword="null"/> when the hash is unknown, the token is revoked, or the
    /// token has expired.
    /// </returns>
    Task<RefreshToken?> ValidateRefreshTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken);
}
