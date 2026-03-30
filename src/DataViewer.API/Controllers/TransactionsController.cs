using DataViewer.Application.DTOs.Common;
using DataViewer.Application.DTOs.Transactions;
using DataViewer.Application.UseCases.Transactions;
using DataViewer.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DataViewer.API.Controllers;

/// <summary>
/// HTTP transaction search and retrieval endpoints
/// </summary>
[ApiController]
[Route("api/v1/transactions")]
[Authorize]
[Produces("application/json")]
public class TransactionsController : ControllerBase
{
    private readonly SearchTransactionsUseCase _searchTransactionsUseCase;
    private readonly GetTransactionDetailUseCase _getTransactionDetailUseCase;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        SearchTransactionsUseCase searchTransactionsUseCase,
        GetTransactionDetailUseCase getTransactionDetailUseCase,
        ILogger<TransactionsController> logger)
    {
        _searchTransactionsUseCase = searchTransactionsUseCase;
        _getTransactionDetailUseCase = getTransactionDetailUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Searches for HTTP transaction records with optional filters
    /// </summary>
    /// <param name="profileId">The credential profile ID to use for S3 access. When null, the active profile is used.</param>
    /// <param name="statusCode">Exact HTTP status code to filter by (e.g. 200, 404). Takes precedence over statusClass.</param>
    /// <param name="statusClass">HTTP status class prefix (1-5) to filter by range of codes. Ignored when statusCode is supplied.</param>
    /// <param name="method">HTTP method to filter by (e.g. GET, POST, DELETE). Case-insensitive comparison.</param>
    /// <param name="urlPrefix">URL path prefix to scope results (e.g. /api/orders). Only transactions whose URL path starts with this value are returned.</param>
    /// <param name="startDate">Inclusive lower bound on transaction timestamp (UTC with explicit offset).</param>
    /// <param name="endDate">Inclusive upper bound on transaction timestamp (UTC with explicit offset).</param>
    /// <param name="page">1-based page number. Must be at least 1. Defaults to 1.</param>
    /// <param name="pageSize">Number of records per page. Must be between 1 and 200. Defaults to 50.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged search results with transaction summaries</returns>
    /// <remarks>
    /// <para>
    /// Searches S3 for HTTP transaction records matching the supplied filter criteria.
    /// All filter parameters are optional. Pagination defaults to page 1 with 50 records per page.
    /// </para>
    /// <para>
    /// The search operation is audit-logged BEFORE retrieving S3 data, ensuring every search
    /// appears in the audit trail regardless of whether S3 access succeeds (ADR-009).
    /// </para>
    /// <para>
    /// Filter criteria are applied in-process after listing S3 objects, as S3 ListObjects
    /// provides no server-side filtering beyond key prefix.
    /// </para>
    /// </remarks>
    /// <response code="200">Successfully retrieved search results</response>
    /// <response code="400">Invalid request - validation errors</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - specified profile does not exist or no active profile available</response>
    /// <response code="500">Internal server error - audit or S3 failure</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResultDto<TransactionSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PagedResultDto<TransactionSummaryDto>>> SearchTransactions(
        [FromQuery] Guid? profileId,
        [FromQuery] int? statusCode,
        [FromQuery] string? statusClass,
        [FromQuery] string? method,
        [FromQuery] string? urlPrefix,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
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

        // Build the search request DTO
        var request = new SearchTransactionsRequestDto
        {
            ProfileId = profileId,
            StatusCode = statusCode,
            StatusClass = statusClass,
            Method = method,
            UrlPrefix = urlPrefix,
            FromDate = startDate,
            ToDate = endDate,
            Page = page,
            PageSize = pageSize
        };

        try
        {
            var results = await _searchTransactionsUseCase.ExecuteAsync(
                request,
                userId,
                ipAddress,
                cancellationToken);

            return Ok(results);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Credential profile not found during transaction search: {ProfileId}",
                profileId?.ToString() ?? "Active");
            return NotFound(new { message = ex.Message });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure during transaction search");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the search operation." });
        }
        catch (S3AccessException ex)
        {
            _logger.LogError(ex, "S3 access failure during transaction search");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while accessing S3 storage." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during transaction search");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Retrieves the full detail of a single HTTP transaction by its S3 key
    /// </summary>
    /// <param name="s3Key">The full S3 object key of the transaction to retrieve (URL-encoded, may contain slashes)</param>
    /// <param name="profileId">The credential profile ID to use for S3 access. When null, the active profile is used.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Full transaction detail including request/response headers, bodies, and metadata</returns>
    /// <remarks>
    /// <para>
    /// Retrieves a single transaction record from S3, decompresses gzipped bodies, truncates
    /// bodies to the configured size cap, detects content types, and returns the full parsed
    /// transaction with all headers and body content.
    /// </para>
    /// <para>
    /// The s3Key parameter must be URL-encoded in the request URL. ASP.NET Core automatically
    /// decodes it before passing to this method. The key may contain slashes (/).
    /// </para>
    /// <para>
    /// The view operation is audit-logged BEFORE retrieving the S3 object, ensuring every
    /// view attempt appears in the audit trail regardless of whether retrieval succeeds (ADR-009).
    /// </para>
    /// </remarks>
    /// <response code="200">Successfully retrieved transaction detail</response>
    /// <response code="400">Invalid request - validation errors</response>
    /// <response code="401">Unauthorized - valid JWT required</response>
    /// <response code="404">Not found - transaction or profile does not exist</response>
    /// <response code="500">Internal server error - audit, S3, or parsing failure</response>
    [HttpGet("{*s3Key}")]
    [ProducesResponseType(typeof(TransactionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TransactionDetailDto>> GetTransactionDetail(
        [FromRoute] string s3Key,
        [FromQuery] Guid? profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(s3Key))
        {
            return BadRequest(new { message = "S3 key must not be empty" });
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user ID in token" });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await _getTransactionDetailUseCase.ExecuteAsync(
                s3Key,
                profileId,
                userId,
                ipAddress,
                cancellationToken);

            return Ok(result);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Transaction or profile not found: S3Key={S3Key}, ProfileId={ProfileId}",
                s3Key, profileId?.ToString() ?? "Active");
            return NotFound(new { message = ex.Message });
        }
        catch (AuditFailureException ex)
        {
            _logger.LogError(ex, "Audit failure while viewing transaction: {S3Key}", s3Key);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while auditing the view operation." });
        }
        catch (S3AccessException ex)
        {
            _logger.LogError(ex, "S3 access failure while retrieving transaction: {S3Key}", s3Key);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while accessing S3 storage." });
        }
        catch (TransactionParseException ex)
        {
            _logger.LogError(ex, "Failed to parse transaction file: {S3Key}", s3Key);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while parsing the transaction data." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while retrieving transaction detail: {S3Key}", s3Key);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }
}
