namespace DataViewer.Application.UseCases.Auth;

using DataViewer.Application.DTOs.Auth;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates the token-refresh flow for the POST /api/auth/refresh endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>Token rotation security pattern:</b>
/// This use case implements automatic token rotation (Product Spec § FR-03) to
/// mitigate replay attacks:
/// <list type="number">
///   <item>
///     <description>
///       The client presents an existing (valid) refresh token.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IRefreshTokenRepository.ValidateAndRevokeAsync"/> atomically
///       validates the token AND immediately revokes it in the same database
///       transaction, ensuring that each refresh token is single-use.
///     </description>
///   </item>
///   <item>
///     <description>
///       A new refresh token is generated and stored, along with a new access token.
///     </description>
///   </item>
///   <item>
///     <description>
///       Both new tokens are returned to the client. The old refresh token is now
///       permanently invalid and cannot be reused even if intercepted.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Replay attack protection:</b>
/// If an attacker intercepts and replays an old refresh token, the
/// <see cref="IRefreshTokenRepository.ValidateAndRevokeAsync"/> call will return
/// <see langword="null"/> (because the token was already revoked during the legitimate
/// refresh operation), causing this method to throw <see cref="UnauthorizedException"/>.
/// No new tokens are issued to the attacker.
/// </para>
///
/// <para>
/// <b>Stateless access tokens:</b>
/// The access token is never validated against the database during its lifetime —
/// it is cryptographically signed and its <c>exp</c> claim is evaluated locally by
/// the JWT middleware. This use case only validates and rotates refresh tokens.
/// </para>
/// </remarks>
public sealed class RefreshTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly ISystemSettingsRepository _systemSettingsRepository;

    public RefreshTokenUseCase(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        ISystemSettingsRepository systemSettingsRepository)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
        _systemSettingsRepository = systemSettingsRepository;
    }

    /// <summary>
    /// Validates a refresh token and issues a new access token + refresh token pair.
    /// </summary>
    /// <param name="request">The refresh request containing the current refresh token.</param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="LoginResponseDto"/> containing the NEW access token, NEW refresh token,
    /// and expiry timestamp. The old refresh token is permanently revoked.
    /// </returns>
    /// <exception cref="UnauthorizedException">
    /// Thrown when:
    /// <list type="bullet">
    ///   <item><description>The refresh token is invalid (unknown hash).</description></item>
    ///   <item><description>The refresh token has expired.</description></item>
    ///   <item><description>The refresh token has already been revoked (replay attack).</description></item>
    /// </list>
    /// In all cases a generic "Invalid or expired refresh token" message is returned
    /// without disclosing the specific failure reason, preventing token-validity probing.
    /// </exception>
    public async Task<LoginResponseDto> ExecuteAsync(
        RefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        // Load system settings for JWT lifetime configuration
        var settings = await _systemSettingsRepository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("SystemSettings not seeded in the database.");

        // Compute SHA-256 hash of the raw refresh token presented by the client
        var tokenHash = _tokenService.GetRefreshTokenHash(request.RefreshToken);

        // Token rotation: Validate AND revoke the old token atomically
        // Returns null if token is invalid, expired, or already revoked
        var validatedToken = await _refreshTokenRepository.ValidateAndRevokeAsync(tokenHash, cancellationToken);

        if (validatedToken is null)
        {
            // Token is invalid, expired, or already revoked (potential replay attack)
            // Throw generic error without revealing specific failure reason
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        // Token was valid and is now revoked — proceed with issuing new tokens
        var user = validatedToken.User;  // Navigation property populated by ValidateAndRevokeAsync

        // Additional security check: Ensure the user account is not locked
        // (A locked user should not be able to refresh tokens)
        var utcNow = DateTime.UtcNow;
        if (user.IsEffectivelyLocked(utcNow))
        {
            // Account is locked — do not issue new tokens
            var lockoutUntil = user.LockoutUntil.HasValue
                ? new DateTimeOffset(user.LockoutUntil.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;

            throw new UnauthorizedException("Account is locked. Contact administrator.", lockoutUntil);
        }

        // Generate NEW tokens (rotation complete)
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenHash = _tokenService.GetRefreshTokenHash(newRefreshToken);

        // Calculate token expiry times
        var accessTokenExpiresAt = utcNow.AddMinutes(settings.JwtAccessTokenMinutes);
        var refreshTokenExpiresAt = utcNow.AddHours(settings.JwtRefreshTokenHours);

        // Store NEW refresh token hash (never the raw token)
        await _refreshTokenRepository.StoreAsync(
            user.Id,
            newRefreshTokenHash,
            refreshTokenExpiresAt,
            cancellationToken);

        // Return NEW tokens to client
        // Note: No audit entry is written for refresh operations (ADR-009 scope: Login/Logout only)
        return new LoginResponseDto
        {
            AccessToken = newAccessToken,
            ExpiresAt = new DateTimeOffset(accessTokenExpiresAt, TimeSpan.Zero),
            RefreshToken = newRefreshToken  // Raw token returned once; never stored
        };
    }
}
