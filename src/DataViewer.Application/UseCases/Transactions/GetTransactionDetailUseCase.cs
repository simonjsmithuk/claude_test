namespace DataViewer.Application.UseCases.Transactions;

using System.Text;
using DataViewer.Application.DTOs.Transactions;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Orchestrates retrieving the full detail of a single HTTP transaction record from S3,
/// including decompression, parsing, body truncation, and content-type detection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Execution flow:</b>
/// <list type="number">
///   <item>
///     <description>
///       Write audit entry FIRST via <see cref="IAuditService.LogViewAsync"/>.
///       If this fails, the retrieval is aborted and no data is returned (ADR-009).
///     </description>
///   </item>
///   <item>
///     <description>
///       Retrieve S3 object bytes via <see cref="IS3Service.GetObjectAsync"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Parse the transaction file via <see cref="ITransactionParser.Parse"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Decompress gzipped request/response bodies via <see cref="IGzipDecompressor"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Truncate decompressed bodies via <see cref="IBodyTruncator"/> to respect size cap.
///     </description>
///   </item>
///   <item>
///     <description>
///       Detect content types via <see cref="IContentTypeDetector"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Return <see cref="TransactionDetailDto"/> with all fields populated.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Audit-first contract (ADR-009):</b>
/// The audit entry is written BEFORE retrieving the S3 object, ensuring every view
/// operation appears in the audit trail regardless of whether the retrieval succeeds.
/// If audit write fails, an <see cref="AuditFailureException"/> is thrown and no data
/// is returned to the caller.
/// </para>
/// </remarks>
public sealed class GetTransactionDetailUseCase
{
    private readonly IS3Service _s3Service;
    private readonly ITransactionParser _transactionParser;
    private readonly IGzipDecompressor _gzipDecompressor;
    private readonly IBodyTruncator _bodyTruncator;
    private readonly IContentTypeDetector _contentTypeDetector;
    private readonly IAuditService _auditService;
    private readonly ICredentialProfileRepository _credentialProfileRepository;

    public GetTransactionDetailUseCase(
        IS3Service s3Service,
        ITransactionParser transactionParser,
        IGzipDecompressor gzipDecompressor,
        IBodyTruncator bodyTruncator,
        IContentTypeDetector contentTypeDetector,
        IAuditService auditService,
        ICredentialProfileRepository credentialProfileRepository)
    {
        _s3Service = s3Service;
        _transactionParser = transactionParser;
        _gzipDecompressor = gzipDecompressor;
        _bodyTruncator = bodyTruncator;
        _contentTypeDetector = contentTypeDetector;
        _auditService = auditService;
        _credentialProfileRepository = credentialProfileRepository;
    }

    /// <summary>
    /// Retrieves the full detail of a single transaction record by its S3 key.
    /// </summary>
    /// <param name="s3Key">The full S3 object key of the transaction to retrieve.</param>
    /// <param name="profileId">
    /// The credential profile ID to use for S3 access. When <see langword="null"/>,
    /// the active profile is used.
    /// </param>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the user viewing this transaction,
    /// extracted from the authenticated JWT access token by the API layer.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address for audit logging.
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="TransactionDetailDto"/> containing the full parsed transaction with
    /// headers, bodies (decompressed and truncated), content types, and metadata.
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when:
    /// <list type="bullet">
    ///   <item><description>The specified S3 object does not exist.</description></item>
    ///   <item><description>The specified profile ID does not exist.</description></item>
    ///   <item><description>No active profile exists when profileId is null.</description></item>
    /// </list>
    /// </exception>
    /// <exception cref="AuditFailureException">
    /// Thrown when the View audit entry cannot be persisted. No transaction data is
    /// returned in this case, enforcing the audit-first guarantee.
    /// </exception>
    public async Task<TransactionDetailDto> ExecuteAsync(
        string s3Key,
        Guid? profileId,
        Guid userId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Resolve credential profile (from parameter or active default)
        var profile = profileId.HasValue
            ? await _credentialProfileRepository.GetByIdAsync(profileId.Value, cancellationToken)
            : await _credentialProfileRepository.GetActiveAsync(cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            var errorMsg = profileId.HasValue
                ? $"Credential profile with ID '{profileId.Value}' not found."
                : "No active credential profile is configured. Please activate a profile first.";
            throw new NotFoundException(errorMsg);
        }

        // Step 1: Audit-first - Write View audit entry BEFORE retrieving S3 object
        await _auditService.LogViewAsync(
            userId,
            ipAddress,
            s3Key,
            cancellationToken);

        // Step 2: Retrieve S3 object bytes
        byte[] s3ObjectBytes;
        try
        {
            s3ObjectBytes = await _s3Service.GetObjectAsync(profile.Id, s3Key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // S3 object not found or other S3 error
            throw new NotFoundException(
                $"Transaction with S3 key '{s3Key}' not found.", ex);
        }

        // Step 3: Parse the transaction file (this handles gzip decompression of the file itself)
        var parsed = _transactionParser.Parse(s3ObjectBytes);

        // Step 4-6: Process request body (decompress if needed, truncate, detect content type)
        var (requestBody, requestContentType, requestTruncated) = await ProcessBodyAsync(
            parsed.RequestBody,
            parsed.RequestHeaders,
            parsed.IsRequestBodyTruncated,
            cancellationToken);

        // Step 4-6: Process response body (decompress if needed, truncate, detect content type)
        var (responseBody, responseContentType, responseTruncated) = await ProcessBodyAsync(
            parsed.ResponseBody,
            parsed.ResponseHeaders,
            parsed.IsResponseBodyTruncated,
            cancellationToken);

        // Step 7: Map to DTO
        return new TransactionDetailDto
        {
            Request = new TransactionMessageDto
            {
                Headers = parsed.RequestHeaders,
                Body = requestBody,
                BodyContentType = requestContentType,
                IsBodyTruncated = requestTruncated
            },
            Response = new TransactionMessageDto
            {
                Headers = parsed.ResponseHeaders,
                Body = responseBody,
                BodyContentType = responseContentType,
                IsBodyTruncated = responseTruncated
            },
            Metadata = new TransactionMetadataDto
            {
                S3Key = parsed.Metadata.S3Key,
                CompressedSizeBytes = parsed.Metadata.CompressedSizeBytes,
                DecompressedSizeBytes = parsed.Metadata.DecompressedSizeBytes,
                TimestampUtc = parsed.Metadata.TimestampUtc,
                S3LastModified = parsed.Metadata.S3LastModified
            }
        };
    }

    /// <summary>
    /// Processes a body string: decompresses if gzipped, truncates to size cap, detects content type.
    /// </summary>
    /// <param name="bodyString">The raw body string from the parsed transaction (may be null).</param>
    /// <param name="headers">The headers dictionary to check for Content-Encoding.</param>
    /// <param name="alreadyTruncated">Whether the body was already truncated at capture time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple containing:
    /// - The processed body string (null if input was null)
    /// - The detected content type (null if body is null)
    /// - Whether the body is truncated (combines already-truncated flag with new truncation)
    /// </returns>
    private async Task<(string? Body, Domain.Enums.BodyContentType? ContentType, bool IsTruncated)>
        ProcessBodyAsync(
            string? bodyString,
            IReadOnlyDictionary<string, string> headers,
            bool alreadyTruncated,
            CancellationToken cancellationToken)
    {
        // If no body, return early
        if (bodyString is null)
        {
            return (null, null, false);
        }

        // Convert body string to bytes for processing
        var bodyBytes = Encoding.UTF8.GetBytes(bodyString);

        // Step 4: Check if body is gzipped and decompress if needed
        var contentEncodingHeader = headers.TryGetValue("content-encoding", out var encoding)
            ? encoding
            : null;

        var (decompressedBytes, wasCompressed) = await _gzipDecompressor.DecompressAsync(
            bodyBytes,
            contentEncodingHeader,
            cancellationToken);

        // Step 5: Truncate body if it exceeds size cap
        bool isTruncatedNow;
        using (var bodyStream = new MemoryStream(decompressedBytes))
        {
            var (truncatedBytes, truncated) = await _bodyTruncator.TruncateIfNeededAsync(
                bodyStream,
                cancellationToken);

            decompressedBytes = truncatedBytes;
            isTruncatedNow = truncated;
        }

        // Combine truncation flags: was it truncated at capture time OR just now?
        var isFinallyTruncated = alreadyTruncated || isTruncatedNow;

        // Convert bytes back to string
        var processedBodyString = Encoding.UTF8.GetString(decompressedBytes);

        // Step 6: Detect content type
        var contentTypeHeader = headers.TryGetValue("content-type", out var ct)
            ? ct
            : null;

        var detectedContentType = _contentTypeDetector.Detect(decompressedBytes, contentTypeHeader);

        return (processedBodyString, detectedContentType, isFinallyTruncated);
    }
}
