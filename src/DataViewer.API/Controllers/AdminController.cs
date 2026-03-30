using DataViewer.Application.DTOs.AdminSettings;
using DataViewer.Application.DTOs.UserPreferences;
using DataViewer.Application.UseCases.AdminSettings;
using DataViewer.Application.UseCases.UserPreferences;
using DataViewer.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DataViewer.API.Controllers;

/// <summary>
/// Administrative endpoints for user preferences and system settings management
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly GetUserPreferencesUseCase _getUserPreferencesUseCase;
    private readonly UpdateUserPreferencesUseCase _updateUserPreferencesUseCase;
    private readonly GetSystemSettingsUseCase _getSystemSettingsUseCase;
    private readonly UpdateSystemSettingsUseCase _updateSystemSettingsUseCase;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        GetUserPreferencesUseCase getUserPreferencesUseCase,
        UpdateUserPreferencesUseCase updateUserPreferencesUseCase,
        GetSystemSettingsUseCase getSystemSettingsUseCase,
        UpdateSystemSettingsUseCase updateSystemSettingsUseCase,
        ILogger<AdminController> logger)
    {
        _getUserPreferencesUseCase = getUserPreferencesUseCase;
        _updateUserPreferencesUseCase = updateUserPreferencesUseCase;
        _getSystemSettingsUseCase = getSystemSettingsUseCase;
        _updateSystemSettingsUseCase = updateSystemSettingsUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the authenticated user's UI preferences
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User preferences including default page size, date range, and preferred profile</returns>
    /// <remarks>
    /// Returns the persisted UI preferences for the authenticated user. If the user has never
    /// saved their preferences, returns application-layer defaults (page size = 25, date range = 7 days,
    /// preferred profile = null).
    ///
    /// This endpoint does not create audit entries as reading one's own UI preferences is informational
    /// and non-sensitive.
    /// </remarks>
    /// <response code="200">Successfully retrieved user preferences</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    [HttpGet("preferences")]
    [Authorize]
    [ProducesResponseType(typeof(UserPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserPreferenceDto>> GetUserPreferences(
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user ID in token" });
        }

        try
        {
            var preferences = await _getUserPreferencesUseCase.ExecuteAsync(userId, cancellationToken);
            return Ok(preferences);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve user preferences for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving user preferences." });
        }
    }

    /// <summary>
    /// Updates the authenticated user's UI preferences
    /// </summary>
    /// <param name="request">User preferences to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated user preferences</returns>
    /// <remarks>
    /// Performs a full-replacement upsert of the user's UI preferences. If the user has never saved
    /// preferences before, a new row is inserted. If a row already exists, all fields are updated in place.
    ///
    /// If PreferredProfileId is non-null, validates that a non-deleted credential profile with that ID exists.
    /// If the profile does not exist, returns HTTP 404.
    ///
    /// This endpoint does not create audit entries as updating one's own UI preferences is informational
    /// and does not affect system security posture.
    /// </remarks>
    /// <response code="200">User preferences updated successfully</response>
    /// <response code="400">Invalid request - validation errors</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - preferred credential profile does not exist</response>
    [HttpPut("preferences")]
    [Authorize]
    [ProducesResponseType(typeof(UserPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserPreferenceDto>> UpdateUserPreferences(
        [FromBody] UserPreferenceDto request,
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

        try
        {
            var preferences = await _updateUserPreferencesUseCase.ExecuteAsync(
                userId,
                request,
                cancellationToken);

            return Ok(preferences);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "User {UserId} attempted to set preferred profile to non-existent profile: {ProfileId}",
                userId, request.PreferredProfileId);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user preferences for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while updating user preferences." });
        }
    }

    /// <summary>
    /// Retrieves the current system-wide configuration settings
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System settings including JWT token lifetimes, body size cap, and lockout threshold</returns>
    /// <remarks>
    /// Returns the singleton system settings row (Id = 1) that controls system-wide behavior.
    /// This endpoint is restricted to users with the Admin role.
    ///
    /// The response includes the server-managed UpdatedAt timestamp indicating when the settings
    /// were last modified by an administrator.
    ///
    /// This endpoint does not create audit entries as reading configuration is informational and
    /// does not expose transaction data or change system state.
    /// </remarks>
    /// <response code="200">Successfully retrieved system settings</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="403">Forbidden - Admin role required</response>
    /// <response code="404">Not found - system settings not seeded (fatal deployment error)</response>
    [HttpGet("settings")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(SystemSettingsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SystemSettingsResponseDto>> GetSystemSettings(
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _getSystemSettingsUseCase.ExecuteAsync(cancellationToken);
            return Ok(settings);
        }
        catch (NotFoundException ex)
        {
            _logger.LogCritical(ex, "Fatal configuration error: SystemSettings singleton row not found");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve system settings");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving system settings." });
        }
    }

    /// <summary>
    /// Updates the system-wide configuration settings
    /// </summary>
    /// <param name="request">System settings to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated system settings including the new UpdatedAt timestamp</returns>
    /// <remarks>
    /// Updates the singleton system settings row (Id = 1) with the supplied values. This is a
    /// full-replacement update, not a partial update - all fields must be present in the request.
    /// This endpoint is restricted to users with the Admin role.
    ///
    /// The UpdatedAt timestamp is server-managed and automatically set by the Infrastructure layer's
    /// SaveChanges interceptor.
    ///
    /// Audit-first contract (ADR-009): An UpdateSystemSettings audit entry is written BEFORE the
    /// database update is committed. If the audit write fails, an AuditFailureException is thrown
    /// and the settings are NOT updated.
    /// </remarks>
    /// <response code="200">System settings updated successfully</response>
    /// <response code="400">Invalid request - validation errors</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="403">Forbidden - Admin role required</response>
    /// <response code="404">Not found - system settings not seeded (fatal deployment error)</response>
    /// <response code="500">Internal server error - audit failure</response>
    [HttpPut("settings")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(SystemSettingsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SystemSettingsResponseDto>> UpdateSystemSettings(
        [FromBody] SystemSettingsRequestDto request,
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

        try
        {
            var settings = await _updateSystemSettingsUseCase.ExecuteAsync(
                request,
                userId,
                ipAddress,
                cancellationToken);

            return Ok(settings);
        }
        catch (NotFoundException ex)
        {
            _logger.LogCritical(ex, "Fatal configuration error: SystemSettings singleton row not found during update");
            return NotFound(new { message = ex.Message });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while updating system settings by user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the operation." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating system settings by user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }
}
