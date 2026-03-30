using DataViewer.Application.DTOs.Auth;
using DataViewer.Application.UseCases.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DataViewer.API.Controllers;

/// <summary>
/// Authentication and authorization endpoints
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly LoginUseCase _loginUseCase;
    private readonly LogoutUseCase _logoutUseCase;
    private readonly RefreshTokenUseCase _refreshTokenUseCase;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        LoginUseCase loginUseCase,
        LogoutUseCase logoutUseCase,
        RefreshTokenUseCase refreshTokenUseCase,
        ILogger<AuthController> logger)
    {
        _loginUseCase = loginUseCase;
        _logoutUseCase = logoutUseCase;
        _refreshTokenUseCase = refreshTokenUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Authenticate user and obtain JWT tokens
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT access token and refresh token</returns>
    /// <response code="200">Login successful, returns tokens</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Invalid credentials or account locked</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponseDto>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var response = await _loginUseCase.ExecuteAsync(request, ipAddress, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Login failed for user {UserName} from IP {IpAddress}",
                request.UserName, ipAddress);
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Revoke refresh token and log out user
    /// </summary>
    /// <param name="request">Refresh token to revoke</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Logout confirmation</returns>
    /// <response code="200">Logout successful</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user ID in token" });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await _logoutUseCase.ExecuteAsync(userId, request, ipAddress, cancellationToken);

        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Exchange refresh token for new access token
    /// </summary>
    /// <param name="request">Refresh token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New JWT access token and refresh token</returns>
    /// <response code="200">Token refresh successful, returns new tokens</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Invalid or expired refresh token</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponseDto>> RefreshToken(
        [FromBody] RefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var response = await _refreshTokenUseCase.ExecuteAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Token refresh failed");
            return Unauthorized(new { message = ex.Message });
        }
    }
}
