namespace DataViewer.Application.UseCases.Auth;

using BCrypt.Net;
using DataViewer.Application.DTOs.Auth;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates the full authentication flow for the POST /api/auth/login endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security invariants enforced by this use case:</b>
/// <list type="number">
///   <item>
///     <description>
///       Lockout status is checked FIRST before password verification begins.
///       This prevents an attacker from probing whether a locked account exists
///       via timing differences in the password-verification step.
///     </description>
///   </item>
///   <item>
///     <description>
///       Password verification is performed with <c>BCrypt.Net.BCrypt.Verify</c>.
///       This uses constant-time comparison internally, preventing timing attacks
///       that could leak information about partial password correctness.
///     </description>
///   </item>
///   <item>
///     <description>
///       Failed login attempts are persisted to the database BEFORE checking the
///       lockout threshold. This guarantees that the threshold is evaluated on
///       the updated count, enforcing the configured security policy correctly.
///     </description>
///   </item>
///   <item>
///     <description>
///       When the threshold is reached, an AccountLocked audit entry is written
///       BEFORE the <see cref="UnauthorizedException"/> is thrown, ensuring that
///       the lockout event appears in the audit trail regardless of whether the
///       API layer handles the exception correctly.
///     </description>
///   </item>
///   <item>
///     <description>
///       On successful authentication, a Login audit entry is written BEFORE the
///       DTO is returned to the caller, implementing the audit-first guarantee
///       from ADR-009.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Error disclosure contract:</b>
/// All authentication failures return a generic "Invalid username or password"
/// message via <see cref="UnauthorizedException"/>. Neither the API layer nor this
/// use case should disclose whether the failure was due to a non-existent username,
/// an incorrect password, or other reasons — this prevents username enumeration.
/// The only exception to this rule is the explicit account-locked response which is
/// unavoidable because the HTTP 423 status and Retry-After header inherently disclose
/// that the username is valid and exists.
/// </para>
/// </remarks>
public sealed class LoginUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _auditService;
    private readonly ISystemSettingsRepository _systemSettingsRepository;

    public LoginUseCase(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService _tokenService,
        IAuditService auditService,
        ISystemSettingsRepository systemSettingsRepository)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        this._tokenService = _tokenService;
        _auditService = auditService;
        _systemSettingsRepository = systemSettingsRepository;
    }

    /// <summary>
    /// Authenticates a user and issues JWT access + refresh tokens.
    /// </summary>
    /// <param name="request">Login credentials from the client.</param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="LoginResponseDto"/> containing the access token, refresh token,
    /// and expiry timestamp.
    /// </returns>
    /// <exception cref="UnauthorizedException">
    /// Thrown when:
    /// <list type="bullet">
    ///   <item><description>The username does not exist.</description></item>
    ///   <item><description>The password is incorrect.</description></item>
    ///   <item><description>The account is locked out (with lockout metadata).</description></item>
    /// </list>
    /// </exception>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the Login audit entry cannot be persisted. The tokens are NOT
    /// returned to the caller in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task<LoginResponseDto> ExecuteAsync(
        LoginRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Load system settings for lockout threshold and JWT lifetime configuration
        var settings = await _systemSettingsRepository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("SystemSettings not seeded in the database.");

        // Retrieve user by username (case-insensitive)
        var user = await _userRepository.GetByUsernameAsync(request.UserName, cancellationToken);

        // If user doesn't exist, throw generic error without revealing that the username is invalid
        if (user is null)
        {
            // Write LoginFailed audit entry even though user doesn't exist (logs attack attempts)
            await _auditService.LogAuthAsync(
                Guid.Empty,  // No user ID since user doesn't exist
                ipAddress,
                AuditActionType.LoginFailed,
                cancellationToken);

            throw new UnauthorizedException("Invalid username or password.");
        }

        // Security invariant #1: Check lockout FIRST before any password verification
        var utcNow = DateTime.UtcNow;
        if (user.IsEffectivelyLocked(utcNow))
        {
            // Write LoginFailed audit entry for locked account attempt
            await _auditService.LogAuthAsync(
                user.Id,
                ipAddress,
                AuditActionType.LoginFailed,
                cancellationToken);

            // Throw with lockout metadata so API layer can return HTTP 423 with Retry-After header
            var lockoutUntil = user.LockoutUntil.HasValue
                ? new DateTimeOffset(user.LockoutUntil.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;

            throw new UnauthorizedException("Account is locked. Contact administrator.", lockoutUntil);
        }

        // Security invariant #2: Verify password using BCrypt (constant-time comparison)
        bool passwordValid = BCrypt.Verify(request.Password, user.PasswordHash);

        if (!passwordValid)
        {
            // Security invariant #3: Increment failed count BEFORE checking threshold
            await _userRepository.IncrementFailedLoginAsync(user.Id, cancellationToken);

            // Re-fetch user to get updated FailedLoginCount
            user = await _userRepository.GetByIdAsync(user.Id, cancellationToken)
                ?? throw new InvalidOperationException($"User {user.Id} disappeared during login flow.");

            // Check if threshold reached → lock account
            if (user.FailedLoginCount >= settings.LockoutThreshold && settings.LockoutThreshold > 0)
            {
                // Calculate lockout expiry: 30 minutes from now (fixed duration)
                var lockoutUntil = utcNow.AddMinutes(30);

                // Lock the account
                await _userRepository.LockAsync(user.Id, lockoutUntil, cancellationToken);

                // Security invariant #4: Write AccountLocked audit entry BEFORE throwing
                await _auditService.LogAuthAsync(
                    user.Id,
                    ipAddress,
                    AuditActionType.AccountLocked,
                    cancellationToken);

                throw new UnauthorizedException(
                    "Account is locked due to repeated failed login attempts.",
                    new DateTimeOffset(lockoutUntil, TimeSpan.Zero));
            }

            // Write LoginFailed audit entry
            await _auditService.LogAuthAsync(
                user.Id,
                ipAddress,
                AuditActionType.LoginFailed,
                cancellationToken);

            // Throw generic error without revealing specific failure reason
            throw new UnauthorizedException("Invalid username or password.");
        }

        // ── Successful authentication: reset failed count, update last login ──

        await _userRepository.ResetFailedLoginAsync(user.Id, cancellationToken);

        // Update LastLoginAt timestamp using domain method
        user.RecordSuccessfulLogin(utcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenHash = _tokenService.GetRefreshTokenHash(refreshToken);

        // Calculate token expiry times
        var accessTokenExpiresAt = utcNow.AddMinutes(settings.JwtAccessTokenMinutes);
        var refreshTokenExpiresAt = utcNow.AddHours(settings.JwtRefreshTokenHours);

        // Store refresh token hash (never the raw token)
        await _refreshTokenRepository.StoreAsync(
            user.Id,
            refreshTokenHash,
            refreshTokenExpiresAt,
            cancellationToken);

        // Security invariant #5: Write Login audit entry BEFORE returning tokens (audit-first)
        await _auditService.LogAuthAsync(
            user.Id,
            ipAddress,
            AuditActionType.Login,
            cancellationToken);

        // Return tokens to client
        return new LoginResponseDto
        {
            AccessToken = accessToken,
            ExpiresAt = new DateTimeOffset(accessTokenExpiresAt, TimeSpan.Zero),
            RefreshToken = refreshToken  // Raw token returned once; never stored
        };
    }
}
