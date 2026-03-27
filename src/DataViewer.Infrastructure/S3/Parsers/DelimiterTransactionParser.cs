namespace DataViewer.Infrastructure.S3.Parsers;

using System.Buffers;
using System.IO.Compression;
using System.Text;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;
using DataViewer.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Parses a gzip-compressed S3 transaction file whose decompressed content uses
/// text-based delimiter markers to separate the HTTP request and response sections.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Expected file format (after gzip decompression):</strong>
/// <code>
/// --- REQUEST ---
/// {METHOD} {path} HTTP/{version}
/// {Header-Name}: {header value}
/// …
///
/// {optional request body bytes}
/// --- RESPONSE ---
/// HTTP/{version} {status-code} {reason-phrase}
/// {Header-Name}: {header value}
/// …
///
/// {optional response body bytes}
/// </code>
/// Line endings may be CRLF (<c>\r\n</c>) or LF (<c>\n</c>); both are handled.
/// The blank line that separates headers from the body follows standard HTTP
/// message framing rules (RFC 7230).
/// </para>
///
/// <para>
/// <strong>Allocation strategy:</strong>
/// The raw gzip bytes are decompressed into a rented <see cref="byte"/> buffer
/// via <see cref="ArrayPool{T}"/>.  The decompressed bytes are then sliced into
/// two <see cref="ReadOnlyMemory{T}"/> windows (request section / response section)
/// without copying, and each section is parsed entirely with
/// <see cref="ReadOnlySpan{T}"/> operations.  Only the final decoded string values
/// (headers, body text) are allocated as managed strings.
/// </para>
///
/// <para>
/// <strong>Zip-bomb protection:</strong>
/// <see cref="TransactionParserOptions.MaxDecompressedSizeBytes"/> is enforced inside
/// the decompression loop. If the expanding stream exceeds the configured ceiling a
/// <see cref="TransactionParseException"/> is thrown immediately, and the rented buffer
/// is returned to the pool before the exception propagates.
/// </para>
///
/// <para>
/// <strong>Truncation flags:</strong>
/// The delimiter format contains no embedded body-length field, so the parser has no
/// mechanism to detect capture-time truncation. Both
/// <see cref="ParsedTransaction.IsRequestBodyTruncated"/> and
/// <see cref="ParsedTransaction.IsResponseBodyTruncated"/> are always returned as
/// <see langword="false"/>. The traffic capture agent is expected to embed an explicit
/// truncation marker in the body text when it clips a payload at the size cap.
/// </para>
/// </remarks>
internal sealed class DelimiterTransactionParser : ITransactionParser
{
    // ── Delimiter marker bytes (UTF-8, no BOM) ────────────────────────────────

    /// <summary>UTF-8 bytes of the request section opening marker.</summary>
    private static readonly byte[] RequestMarker =
        Encoding.UTF8.GetBytes("--- REQUEST ---");

    /// <summary>UTF-8 bytes of the response section opening marker.</summary>
    private static readonly byte[] ResponseMarker =
        Encoding.UTF8.GetBytes("--- RESPONSE ---");

    // ── Encoding ──────────────────────────────────────────────────────────────

    /// <summary>
    /// UTF-8 encoding without BOM, used to decode header lines and body text.
    /// Invalid byte sequences are replaced with the Unicode replacement character
    /// (U+FFFD) rather than throwing, ensuring partial results can always be returned.
    /// </summary>
    private static readonly Encoding Utf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    // ── Sentinel used in metadata stubs ──────────────────────────────────────

    /// <summary>
    /// Sentinel value used for timestamp fields that the caller must overlay.
    /// Using <see cref="DateTimeOffset.MinValue"/> (year 0001) instead of
    /// <c>UtcNow</c> ensures that a missed overlay produces an obviously wrong
    /// timestamp rather than a subtly plausible one.
    /// </summary>
    private static readonly DateTimeOffset TimestampSentinel = DateTimeOffset.MinValue;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ILogger<DelimiterTransactionParser> _logger;
    private readonly TransactionParserOptions _options;

    /// <summary>
    /// Initialises the parser with its required dependencies.
    /// </summary>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    /// <param name="options">
    /// Strongly-typed parser options, including the zip-bomb protection ceiling
    /// (<see cref="TransactionParserOptions.MaxDecompressedSizeBytes"/>).
    /// </param>
    public DelimiterTransactionParser(
        ILogger<DelimiterTransactionParser> logger,
        IOptions<TransactionParserOptions> options)
    {
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    // ── ITransactionParser ────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <exception cref="TransactionParseException">
    /// Thrown when <paramref name="data"/> is not a valid gzip stream, when
    /// the decompressed content does not contain both the
    /// <c>--- REQUEST ---</c> and <c>--- RESPONSE ---</c> delimiter markers, or
    /// when the decompressed size exceeds
    /// <see cref="TransactionParserOptions.MaxDecompressedSizeBytes"/>.
    /// </exception>
    public ParsedTransaction Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            throw new TransactionParseException(
                "Cannot parse an empty byte array: the S3 transaction file contains no data.");
        }

        // ── Step 1: Decompress ────────────────────────────────────────────────
        // Returns a ReadOnlyMemory<byte> whose backing array was rented from
        // ArrayPool<byte>.Shared. We MUST return it in the finally block below.
        var (decompressed, backingArray) = DecompressGzip(data);

        try
        {
            // ── Step 2: Locate delimiter markers ─────────────────────────────
            var span = decompressed.Span;

            var requestMarkerStart = span.IndexOf(RequestMarker.AsSpan());
            if (requestMarkerStart < 0)
            {
                throw new TransactionParseException(
                    "Transaction file is missing the required '--- REQUEST ---' delimiter marker. "
                    + "Ensure the file was generated by the expected traffic capture agent. "
                    + $"Decompressed size: {span.Length} bytes.");
            }

            // ── BUG-2 FIX: Search for the RESPONSE marker only in the region
            // that begins after the request marker's trailing line ending.
            // This prevents a request body that happens to contain the literal
            // text "--- RESPONSE ---" (e.g. a captured HTTP exchange description)
            // from being misidentified as the real section delimiter.
            int requestSectionStart =
                AdvancePastLineEnding(span, requestMarkerStart + RequestMarker.Length);

            // Search for RESPONSE marker starting from the first byte of the request
            // section content so it can never resolve to a position inside the marker
            // line itself. Returned offset is relative to span[requestSectionStart..],
            // so we add requestSectionStart to make it absolute.
            var responseMarkerRelative =
                span[requestSectionStart..].IndexOf(ResponseMarker.AsSpan());

            if (responseMarkerRelative < 0)
            {
                throw new TransactionParseException(
                    "Transaction file is missing the required '--- RESPONSE ---' delimiter marker. "
                    + "Ensure the file was generated by the expected traffic capture agent. "
                    + $"Decompressed size: {span.Length} bytes.");
            }

            var responseMarkerStart = requestSectionStart + responseMarkerRelative;

            // Order check: responseMarkerStart is always > requestMarkerStart by
            // construction (we searched from requestSectionStart), but verify
            // explicitly to guard against a zero-length request section edge case.
            if (responseMarkerStart <= requestMarkerStart)
            {
                throw new TransactionParseException(
                    "Transaction file delimiter markers are out of order: "
                    + $"'--- RESPONSE ---' marker (offset {responseMarkerStart}) precedes or "
                    + $"coincides with '--- REQUEST ---' marker (offset {requestMarkerStart}). "
                    + "The file content may be corrupted.");
            }

            _logger.LogDebug(
                "DelimiterTransactionParser: markers found — REQUEST at {RequestOffset}, RESPONSE at {ResponseOffset}, total {TotalBytes} decompressed bytes",
                requestMarkerStart,
                responseMarkerStart,
                span.Length);

            // ── Step 3: Slice sections (zero-copy ReadOnlyMemory windows) ─────
            // requestSectionStart is already computed above (BUG-2 fix).
            // The request section ends at the start of the RESPONSE marker;
            // the response section extends to EOF.
            int requestSectionEnd = responseMarkerStart;

            int responseSectionStart =
                AdvancePastLineEnding(span, responseMarkerStart + ResponseMarker.Length);

            var requestSection =
                decompressed.Slice(requestSectionStart, requestSectionEnd - requestSectionStart);
            var responseSection =
                decompressed.Slice(responseSectionStart);

            // ── Step 4: Parse each section ────────────────────────────────────
            // ParseSection returns offsets and lengths instead of spans
            var (requestLine, requestHeaders, reqBodyOffset, reqBodyLen)   = ParseSection(requestSection.Span);
            var (responseLine, responseHeaders, resBodyOffset, resBodyLen) = ParseSection(responseSection.Span);

            // ── Step 5: Decode start lines ────────────────────────────────────
            var (method, requestPath, _) = ParseRequestLine(requestLine);
            var (_, statusCode, _)       = ParseStatusLine(responseLine);

            _logger.LogDebug(
                "DelimiterTransactionParser: successfully parsed {Method} {Path} → {StatusCode}",
                method,
                requestPath,
                statusCode);

            // ── Step 6: Decode bodies to strings BEFORE returning the pool buffer
            // Extract the body spans from the section using the offsets and lengths
            var requestBodySpan = reqBodyLen > 0
                ? TrimTrailingNewlines(requestSection.Span.Slice(reqBodyOffset, reqBodyLen))
                : ReadOnlySpan<byte>.Empty;

            var responseBodySpan = resBodyLen > 0
                ? TrimTrailingNewlines(responseSection.Span.Slice(resBodyOffset, resBodyLen))
                : ReadOnlySpan<byte>.Empty;

            string? requestBody = requestBodySpan.IsEmpty
                ? null
                : Utf8.GetString(requestBodySpan);

            string? responseBody = responseBodySpan.IsEmpty
                ? null
                : Utf8.GetString(responseBodySpan);

            // ── Step 7: Detect body content types ─────────────────────────────
            requestHeaders.TryGetValue("content-type", out var reqContentTypeHeader);
            responseHeaders.TryGetValue("content-type", out var resContentTypeHeader);

            BodyContentType? requestBodyContentType = requestBody is not null
                ? BodyContentTypeDetector.Detect(requestBody, reqContentTypeHeader)
                : null;

            BodyContentType? responseBodyContentType = responseBody is not null
                ? BodyContentTypeDetector.Detect(responseBody, resContentTypeHeader)
                : null;

            // ── Step 8: Build metadata stub ───────────────────────────────────
            // ASSUMPTION: The caller (application layer) is expected to overlay the
            // stub metadata (S3Key, TimestampUtc, S3LastModified) with values from the
            // S3 object listing before constructing the final API response DTO.
            // ITransactionParser.Parse receives only raw bytes, so these fields cannot
            // be populated here; CompressedSizeBytes and DecompressedSizeBytes are
            // correctly captured from the byte arrays we already have.
            //
            // TimestampSentinel (DateTimeOffset.MinValue / year 0001) is used instead
            // of DateTimeOffset.UtcNow so that a missed overlay produces an unmistakably
            // wrong timestamp in logs/responses rather than a subtly plausible one.
            var metadata = new TransactionMetadata(
                S3Key:                 string.Empty,      // overlay required by caller
                Method:                method,
                StatusCode:            statusCode,
                UrlPath:               requestPath,
                TimestampUtc:          TimestampSentinel, // overlay required by caller
                CompressedSizeBytes:   data.Length,
                DecompressedSizeBytes: span.Length,
                S3LastModified:        TimestampSentinel  // overlay required by caller
            );

            return new ParsedTransaction(
                RequestHeaders:          requestHeaders.AsReadOnly(),
                ResponseHeaders:         responseHeaders.AsReadOnly(),
                RequestBody:             requestBody,
                ResponseBody:            responseBody,
                RequestBodyContentType:  requestBodyContentType,
                ResponseBodyContentType: responseBodyContentType,
                IsRequestBodyTruncated:  false,  // no embedded length field in delimiter format
                IsResponseBodyTruncated: false,  // no embedded length field in delimiter format
                Metadata:                metadata
            );
        }
        finally
        {
            // Return the rented decompression buffer to the pool.
            // All Span<byte> and ReadOnlySpan<byte> views derived from this buffer
            // must not be used after this point (the body strings are already decoded).
            ArrayPool<byte>.Shared.Return(backingArray);
        }
    }

    // ── Private parsing helpers ───────────────────────────────────────────────

    /// <summary>
    /// Decompresses the gzip-compressed <paramref name="data"/> into a buffer
    /// rented from <see cref="ArrayPool{byte}.Shared"/>.
    /// </summary>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>Memory</c> — a <see cref="ReadOnlyMemory{T}"/> window over exactly
    ///       the decompressed bytes (length = decompressed byte count).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>BackingArray</c> — the rented <see cref="byte"/>[] that backs the
    ///       memory window.  The caller <strong>must</strong> return this array via
    ///       <see cref="ArrayPool{byte}.Return"/> when it is no longer needed.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    /// <exception cref="TransactionParseException">
    /// Thrown when <paramref name="data"/> is not a valid gzip stream, or when
    /// the decompressed output would exceed
    /// <see cref="TransactionParserOptions.MaxDecompressedSizeBytes"/> (zip-bomb guard).
    /// </exception>
    private (ReadOnlyMemory<byte> Memory, byte[] BackingArray) DecompressGzip(byte[] data)
    {
        int maxBytes = _options.MaxDecompressedSizeBytes;

        // Initial capacity heuristic: text gzip typically achieves 3–10× compression.
        // Start at 8× and grow the rented buffer dynamically if needed, up to maxBytes.
        int estimatedSize = Math.Max(Math.Min(data.Length * 8, maxBytes), 4096);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
        int bytesWritten = 0;

        try
        {
            using var inputStream = new MemoryStream(data, writable: false);
            using var gzipStream  = new GZipStream(inputStream, CompressionMode.Decompress);

            while (true)
            {
                // ── BUG-1 / SEC-1 FIX: Enforce the zip-bomb decompression ceiling.
                // Check BEFORE reading the next chunk so the check fires even when the
                // stream fills the buffer exactly up to the limit on the previous iteration.
                if (bytesWritten > maxBytes)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw new TransactionParseException(
                        $"Decompressed size exceeds the configured limit of {maxBytes:N0} bytes. "
                        + "This may indicate a zip-bomb payload. "
                        + "Adjust S3:MaxDecompressedSizeBytes in appsettings.json if the file is legitimate.");
                }

                // Grow the rented buffer when fewer than 4 KB remain.
                if (bytesWritten >= buffer.Length - 4096)
                {
                    // ── SEC-2 FIX: Cap the doubling growth factor at maxBytes + one read
                    // buffer (4096) so that a single rental can never grow unboundedly
                    // beyond the configured ceiling — a secondary defence against OOM
                    // when concurrent requests are in flight.
                    int newSize = Math.Min(buffer.Length * 2, maxBytes + 4096);
                    var larger  = ArrayPool<byte>.Shared.Rent(newSize);
                    Buffer.BlockCopy(buffer, 0, larger, 0, bytesWritten);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                int read = gzipStream.Read(buffer, bytesWritten, buffer.Length - bytesWritten);
                if (read == 0)
                {
                    break; // end of gzip stream
                }

                bytesWritten += read;
            }
        }
        catch (InvalidDataException ex)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw new TransactionParseException(
                "The S3 transaction file is not a valid gzip stream. "
                + "Ensure the file was gzip-compressed by the traffic capture agent. "
                + $"Input size: {data.Length} bytes.",
                ex);
        }
        catch (TransactionParseException)
        {
            // The zip-bomb guard above already returned the buffer before throwing;
            // re-throw without double-returning the buffer.
            throw;
        }
        catch
        {
            // Return the buffer for any other unexpected exception before propagating.
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        _logger.LogDebug(
            "DelimiterTransactionParser: decompressed {CompressedBytes} compressed bytes → {DecompressedBytes} bytes",
            data.Length,
            bytesWritten);

        // Return a view over exactly the populated portion of the rented buffer.
        return (new ReadOnlyMemory<byte>(buffer, 0, bytesWritten), buffer);
    }

    /// <summary>
    /// Parses a single HTTP section (request or response) from a
    /// <see cref="ReadOnlySpan{T}"/> of decompressed bytes, extracting the start
    /// line, headers dictionary, and raw body bytes as a sliced ReadOnlyMemory.
    /// </summary>
    /// <param name="section">
    /// Bytes of the section, starting immediately after the delimiter marker's
    /// line ending and ending at the next marker's start (or EOF).
    /// </param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    ///   <item><description><c>StartLine</c> — the decoded request or status line.</description></item>
    ///   <item><description><c>Headers</c> — mutable, lowercase-keyed header dictionary (wrap with <c>.AsReadOnly()</c> before publishing).</description></item>
    ///   <item><description><c>BodyStartOffset</c> — offset in section where body begins.</description></item>
    ///   <item><description><c>BodyLength</c> — length of body in bytes.</description></item>
    /// </list>
    /// </returns>
    private static (string StartLine,
                    Dictionary<string, string> Headers,
                    int BodyStartOffset,
                    int BodyLength)
        ParseSection(ReadOnlySpan<byte> section)
    {
        var remaining = section;
        int bodyStartOffset = 0;

        // ── Start line ────────────────────────────────────────────────────────
        var startLineBytes = ConsumeLine(ref remaining);
        var startLine      = Utf8.GetString(startLineBytes).Trim();

        // ── Headers ───────────────────────────────────────────────────────────
        // Header keys are normalised to lowercase to match the ParsedTransaction
        // contract (XML documentation on the record specifies "normalised to lowercase").
        var headers = new Dictionary<string, string>(
            capacity: 16, comparer: StringComparer.OrdinalIgnoreCase);

        while (!remaining.IsEmpty)
        {
            var lineBytes = ConsumeLine(ref remaining);

            // A blank line terminates the header block (standard HTTP framing).
            if (lineBytes.IsEmpty || IsAllWhitespace(lineBytes))
            {
                break;
            }

            var line       = Utf8.GetString(lineBytes);
            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);

            if (colonIndex <= 0)
            {
                // Skip malformed header lines — partial-result policy.
                continue;
            }

            // Header names cannot contain colons; values may (e.g. Date, Authorization).
            var name  = line[..colonIndex].Trim().ToLowerInvariant();
            var value = line[(colonIndex + 1)..].Trim();

            // Last-write wins for duplicate header names. The capture agent typically
            // writes each header exactly once; de-duplication is not required here.
            headers[name] = value;
        }

        // ── Body ──────────────────────────────────────────────────────────────
        // Calculate the offset where the body starts in the original section
        bodyStartOffset = section.Length - remaining.Length;

        // Trim trailing newlines that the capture agent may append after the body.
        var bodyBytes = TrimTrailingNewlines(remaining);

        return (startLine, headers, bodyStartOffset, bodyBytes.Length);
    }

    /// <summary>
    /// Parses an HTTP/1.x request start line: <c>{METHOD} {request-target} HTTP/{version}</c>.
    /// </summary>
    /// <exception cref="TransactionParseException">
    /// Thrown when the line is empty or cannot be split into three tokens.
    /// </exception>
    private static (string Method, string Path, string HttpVersion) ParseRequestLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new TransactionParseException(
                "Request section is missing the HTTP request line (method, path, HTTP version). "
                + "The section may be empty or the '--- REQUEST ---' marker is misplaced.");
        }

        // Use first-space / last-space split so that request-targets containing
        // spaces (uncommon but possible) are attributed to the path rather than
        // the method or HTTP-version token.
        var firstSpace = line.IndexOf(' ', StringComparison.Ordinal);
        var lastSpace  = line.LastIndexOf(' ');

        if (firstSpace < 0 || lastSpace <= firstSpace)
        {
            throw new TransactionParseException(
                "Malformed HTTP request line — expected '{METHOD} {path} HTTP/{version}', "
                + $"found: '{line}'.");
        }

        var method  = line[..firstSpace].Trim().ToUpperInvariant();
        var path    = line[(firstSpace + 1)..lastSpace].Trim();
        var version = line[(lastSpace + 1)..].Trim();

        return (method, path, version);
    }

    /// <summary>
    /// Parses an HTTP/1.x status line: <c>HTTP/{version} {status-code} {reason-phrase}</c>.
    /// </summary>
    /// <exception cref="TransactionParseException">
    /// Thrown when the line is empty, does not start with <c>HTTP/</c>, or contains
    /// a non-numeric status code.
    /// </exception>
    private static (string HttpVersion, int StatusCode, string ReasonPhrase) ParseStatusLine(
        string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new TransactionParseException(
                "Response section is missing the HTTP status line (HTTP/x.x STATUS reason). "
                + "The section may be empty or the '--- RESPONSE ---' marker is misplaced.");
        }

        if (!line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
        {
            throw new TransactionParseException(
                "Malformed HTTP status line — expected 'HTTP/{version} {status} {reason}', "
                + $"found: '{line}'.");
        }

        var firstSpace = line.IndexOf(' ', StringComparison.Ordinal);
        if (firstSpace < 0)
        {
            throw new TransactionParseException(
                "Malformed HTTP status line — no space after HTTP version token, "
                + $"found: '{line}'.");
        }

        var httpVersion = line[..firstSpace].Trim();
        var rest        = line[(firstSpace + 1)..].Trim();
        var nextSpace   = rest.IndexOf(' ', StringComparison.Ordinal);

        // Reason phrase is optional per RFC 7230 §3.1.2.
        var statusCodeStr = nextSpace < 0 ? rest : rest[..nextSpace];
        var reasonPhrase  = nextSpace < 0 ? string.Empty : rest[(nextSpace + 1)..].Trim();

        if (!int.TryParse(statusCodeStr, out var statusCode) ||
            statusCode is < 100 or > 999)
        {
            throw new TransactionParseException(
                $"Malformed HTTP status line — status code '{statusCodeStr}' is not a valid "
                + $"three-digit integer. Full line: '{line}'.");
        }

        return (httpVersion, statusCode, reasonPhrase);
    }

    // ── Low-level span helpers ────────────────────────────────────────────────

    /// <summary>
    /// Returns the index in <paramref name="span"/> immediately after the CRLF or
    /// LF that follows <paramref name="position"/>, skipping the line terminator.
    /// </summary>
    private static int AdvancePastLineEnding(ReadOnlySpan<byte> span, int position)
    {
        if (position >= span.Length)
        {
            return span.Length;
        }

        // CRLF: skip both bytes.
        if (span[position] == '\r'
            && position + 1 < span.Length
            && span[position + 1] == '\n')
        {
            return position + 2;
        }

        // Bare LF.
        if (span[position] == '\n')
        {
            return position + 1;
        }

        // No immediate line ending — scan forward to the next LF.
        var lf = span[position..].IndexOf((byte)'\n');
        return lf < 0 ? span.Length : position + lf + 1;
    }

    /// <summary>
    /// Reads the next line from <paramref name="remaining"/>, advancing it past
    /// the line ending (CRLF or LF) in place.
    /// </summary>
    /// <returns>
    /// Bytes of the line, excluding the line terminator.
    /// Empty span when <paramref name="remaining"/> is already empty.
    /// </returns>
    private static ReadOnlySpan<byte> ConsumeLine(ref ReadOnlySpan<byte> remaining)
    {
        if (remaining.IsEmpty)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        var lfIndex = remaining.IndexOf((byte)'\n');

        if (lfIndex < 0)
        {
            // No LF found — the rest of the span is the final (unterminated) line.
            var finalLine = remaining;
            remaining = ReadOnlySpan<byte>.Empty;
            // Strip trailing CR if present.
            return finalLine.Length > 0 && finalLine[^1] == '\r'
                ? finalLine[..^1]
                : finalLine;
        }

        // Strip the CR of a CRLF pair if present before the LF.
        var endOfContent = lfIndex > 0 && remaining[lfIndex - 1] == '\r'
            ? lfIndex - 1
            : lfIndex;

        var line  = remaining[..endOfContent];
        remaining = remaining[(lfIndex + 1)..];
        return line;
    }

    /// <summary>
    /// Returns <see langword="true"/> when every byte in <paramref name="bytes"/>
    /// is an ASCII whitespace character (space, tab, CR, or LF).
    /// </summary>
    private static bool IsAllWhitespace(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Trims trailing CR and LF bytes from the end of <paramref name="bytes"/>.
    /// Removes the single trailing blank line that the capture agent may append
    /// after the response body.
    /// </summary>
    private static ReadOnlySpan<byte> TrimTrailingNewlines(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is (byte)'\n' or (byte)'\r')
        {
            end--;
        }

        return bytes[..end];
    }
}
