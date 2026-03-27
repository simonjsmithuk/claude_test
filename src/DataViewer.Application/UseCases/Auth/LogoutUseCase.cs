namespace DataViewer.Application.UseCases.Auth;

using DataViewer.Application.DTOs.Auth;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;

/// <summary>
/// Orchestrates the logout flow for the POST /api/auth/logout endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Logout is implemented by revoking the provided refresh token so that it can no
/// longer be used to obtain new access tokens. The access token itself is NOT
/// revoked or blacklisted (JWT is stateless); clients are responsible for discarding
/// it immediately upon receiving a successful logout response. The access token will
/// continue to grant access until its <c>exp</c> claim timestamp is reached (~15 minutes).
/// </para>
///
/// <para>
/// <b>Single-device logout:</b>
/// This endpoint revokes ONLY the specific refresh token presented in the request,
/// leaving other concurrent sessions (if any) active. This implements a
/// single-device logout pattern.
/// </para>
///
/// <para>
/// To implement a <em>logout-all-devices</em> endpoint, call
/// <see cref="IRefreshTokenRepository.RevokeByUserIdAsync"/> instead — that method
/// revokes all active tokens for a given user.
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// A Logout audit entry is written BEFORE the token-revocation database write is
/// committed, guaranteeing that every logout attempt appears in the audit trail
/// regardless of whether the token was valid or already revoked.
/// </para>
/// </remarks>
public sealed class LogoutUseCase
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _auditService;

    public LogoutUseCase(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        IAuditService auditService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
        _auditService = auditService;
    }

    /// <summary>
    /// Revokes a refresh token to log the user out from a single device/session.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the authenticated user
    /// making the logout request. Extracted from the JWT access token's <c>sub</c> claim
    /// by the API layer and passed through here for audit logging.
    /// </param>
    /// <param name="request">The logout request containing the refresh token to revoke.</param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the token has been revoked and the
    /// audit entry has been committed. No value is returned; the API layer should
    /// respond with <c>HTTP 204 No Content</c> on success.
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the Logout audit entry cannot be persisted. The token revocation
    /// is NOT committed in this case, enforcing the audit-first guarantee.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method does not throw when the provided refresh token is invalid, expired,
    /// or already revoked — it silently succeeds (idempotent logout). This prevents an
    /// attacker from using logout-response timing differences to probe which refresh
    /// tokens are valid.
    /// </para>
    /// </remarks>
    public async Task ExecuteAsync(
        Guid userId,
        LogoutRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Compute SHA-256 hash of the raw refresh token
        var tokenHash = _tokenService.GetRefreshTokenHash(request.RefreshToken);

        // Audit-first: Write Logout audit entry BEFORE revoking the token
        await _auditService.LogAuthAsync(
            userId,
            ipAddress,
            AuditActionType.Logout,
            cancellationToken);

        // Revoke the refresh token (ValidateAndRevokeAsync is atomic: validates + revokes in one round-trip)
        // Note: ValidateAndRevokeAsync returns null if token is invalid/expired/already revoked.
        // We deliberately ignore the return value to implement idempotent logout.
        await _refreshTokenRepository.ValidateAndRevokeAsync(tokenHash, cancellationToken);

        // Success — no return value (API responds with HTTP 204)
    }
}
