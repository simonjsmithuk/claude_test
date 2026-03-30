using DataViewer.Application.DTOs.CredentialProfiles;
using DataViewer.Application.UseCases.CredentialProfiles;
using DataViewer.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DataViewer.API.Controllers;

/// <summary>
/// AWS credential profile management endpoints for administrators
/// </summary>
[ApiController]
[Route("api/v1/credential-profiles")]
[Authorize]
[Produces("application/json")]
public class CredentialProfilesController : ControllerBase
{
    private readonly GetCredentialProfilesUseCase _getCredentialProfilesUseCase;
    private readonly CreateCredentialProfileUseCase _createCredentialProfileUseCase;
    private readonly UpdateCredentialProfileUseCase _updateCredentialProfileUseCase;
    private readonly DeleteCredentialProfileUseCase _deleteCredentialProfileUseCase;
    private readonly TestConnectionUseCase _testConnectionUseCase;
    private readonly ActivateCredentialProfileUseCase _activateCredentialProfileUseCase;
    private readonly ILogger<CredentialProfilesController> _logger;

    public CredentialProfilesController(
        GetCredentialProfilesUseCase getCredentialProfilesUseCase,
        CreateCredentialProfileUseCase createCredentialProfileUseCase,
        UpdateCredentialProfileUseCase updateCredentialProfileUseCase,
        DeleteCredentialProfileUseCase deleteCredentialProfileUseCase,
        TestConnectionUseCase testConnectionUseCase,
        ActivateCredentialProfileUseCase activateCredentialProfileUseCase,
        ILogger<CredentialProfilesController> logger)
    {
        _getCredentialProfilesUseCase = getCredentialProfilesUseCase;
        _createCredentialProfileUseCase = createCredentialProfileUseCase;
        _updateCredentialProfileUseCase = updateCredentialProfileUseCase;
        _deleteCredentialProfileUseCase = deleteCredentialProfileUseCase;
        _testConnectionUseCase = testConnectionUseCase;
        _activateCredentialProfileUseCase = activateCredentialProfileUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all non-deleted AWS credential profiles
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of credential profiles</returns>
    /// <remarks>
    /// Returns all credential profiles that have not been soft-deleted.
    /// The AWS Secret Access Key is never included in the response (security requirement G-04).
    /// </remarks>
    /// <response code="200">Successfully retrieved credential profiles</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CredentialProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CredentialProfileDto>>> GetCredentialProfiles(
        CancellationToken cancellationToken)
    {
        try
        {
            var profiles = await _getCredentialProfilesUseCase.ExecuteAsync(cancellationToken);
            return Ok(profiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve credential profiles");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving credential profiles." });
        }
    }

    /// <summary>
    /// Creates a new AWS credential profile
    /// </summary>
    /// <param name="request">Credential profile creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The newly created credential profile</returns>
    /// <remarks>
    /// Creates a new credential profile with encrypted AWS credentials.
    /// The Secret Access Key is encrypted before storage and is never returned in any response.
    /// If SetAsActive is true, this profile becomes the active default and any previously
    /// active profile is deactivated.
    /// </remarks>
    /// <response code="201">Credential profile created successfully</response>
    /// <response code="400">Invalid request - validation errors</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="409">Conflict - a profile with this name already exists</response>
    /// <response code="500">Internal server error - encryption or audit failure</response>
    [HttpPost]
    [ProducesResponseType(typeof(CredentialProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CredentialProfileDto>> CreateCredentialProfile(
        [FromBody] CreateCredentialProfileDto request,
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
            var profile = await _createCredentialProfileUseCase.ExecuteAsync(
                request,
                userId,
                ipAddress,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetCredentialProfiles),
                new { id = profile.Id },
                profile);
        }
        catch (DuplicateNameException ex)
        {
            _logger.LogWarning(ex, "Attempted to create credential profile with duplicate name: {Name}",
                request.Name);
            return Conflict(new { message = ex.Message });
        }
        catch (EncryptionException ex)
        {
            _logger.LogError(ex, "Encryption failure while creating credential profile: {Name}",
                request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while securing the credentials." });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while creating credential profile: {Name}",
                request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the operation." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating credential profile: {Name}",
                request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Updates an existing AWS credential profile
    /// </summary>
    /// <param name="id">The credential profile ID to update</param>
    /// <param name="request">Partial update request (only non-null fields are applied)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated credential profile</returns>
    /// <remarks>
    /// Updates an existing credential profile. All fields in the request are optional - only
    /// non-null fields are updated. If SecretAccessKey is provided, it is re-encrypted and
    /// replaces the stored value. If null, the existing encrypted secret is preserved.
    /// If SetAsActive is true, this profile becomes the active default.
    /// </remarks>
    /// <response code="200">Credential profile updated successfully</response>
    /// <response code="400">Invalid request - validation errors</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - credential profile does not exist</response>
    /// <response code="409">Conflict - the new name conflicts with an existing profile</response>
    /// <response code="500">Internal server error - encryption or audit failure</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CredentialProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CredentialProfileDto>> UpdateCredentialProfile(
        [FromRoute] Guid id,
        [FromBody] UpdateCredentialProfileDto request,
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
            var profile = await _updateCredentialProfileUseCase.ExecuteAsync(
                id,
                request,
                userId,
                ipAddress,
                cancellationToken);

            return Ok(profile);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Attempted to update non-existent credential profile: {Id}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (DuplicateNameException ex)
        {
            _logger.LogWarning(ex, "Attempted to update credential profile with duplicate name: {Name}",
                request.Name);
            return Conflict(new { message = ex.Message });
        }
        catch (EncryptionException ex)
        {
            _logger.LogError(ex, "Encryption failure while updating credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while securing the credentials." });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while updating credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the operation." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Soft-deletes an AWS credential profile
    /// </summary>
    /// <param name="id">The credential profile ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content</returns>
    /// <remarks>
    /// Performs a soft-delete by setting the IsDeleted flag. The profile is preserved in the
    /// database for audit integrity but excluded from all operational queries. If the profile
    /// is currently active, it is automatically deactivated.
    /// </remarks>
    /// <response code="204">Credential profile deleted successfully</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - credential profile does not exist or is already deleted</response>
    /// <response code="500">Internal server error - audit failure</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteCredentialProfile(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user ID in token" });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            await _deleteCredentialProfileUseCase.ExecuteAsync(
                id,
                userId,
                ipAddress,
                cancellationToken);

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Attempted to delete non-existent credential profile: {Id}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while deleting credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the operation." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deleting credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Tests AWS S3 connectivity for a credential profile
    /// </summary>
    /// <param name="id">The credential profile ID to test</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Connection test result indicating success or failure</returns>
    /// <remarks>
    /// Tests whether the AWS credentials can successfully connect to the configured S3 bucket.
    /// The Secret Access Key is decrypted in-memory for the duration of the test only and is
    /// never persisted, logged, or returned in the response. All connection attempts are audited
    /// regardless of success or failure.
    /// </remarks>
    /// <response code="200">Connection test completed (check IsSuccess in response)</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - credential profile does not exist</response>
    /// <response code="500">Internal server error - encryption or audit failure</response>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(typeof(TestConnectionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TestConnectionResultDto>> TestConnection(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user ID in token" });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await _testConnectionUseCase.ExecuteAsync(
                id,
                userId,
                ipAddress,
                cancellationToken);

            return Ok(result);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Attempted to test connection for non-existent credential profile: {Id}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (EncryptionException ex)
        {
            _logger.LogError(ex, "Decryption failure while testing credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while accessing the credentials." });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while testing credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the operation." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while testing credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Activates a credential profile as the system default for S3 operations
    /// </summary>
    /// <param name="id">The credential profile ID to activate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content</returns>
    /// <remarks>
    /// Sets the specified profile as the active default for all S3 operations. Any previously
    /// active profile is automatically deactivated. The system ensures at most one profile is
    /// active at any time through an atomic database transaction.
    /// </remarks>
    /// <response code="204">Credential profile activated successfully</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - credential profile does not exist</response>
    /// <response code="500">Internal server error - audit failure</response>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ActivateCredentialProfile(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user ID in token" });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            await _activateCredentialProfileUseCase.ExecuteAsync(
                id,
                userId,
                ipAddress,
                cancellationToken);

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Attempted to activate non-existent credential profile: {Id}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while activating credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the operation." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while activating credential profile: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }
}
