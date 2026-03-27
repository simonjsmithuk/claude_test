namespace DataViewer.Infrastructure.S3.MetadataExtractors;

using System.Globalization;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Extracts <see cref="TransactionMetadata"/> by parsing conventions encoded
/// directly into the S3 object key path.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Expected key pattern:</strong>
/// <code>
/// {prefix/}yyyy/MM/dd/{METHOD}/{STATUS}/{uuid}.{ext}
/// </code>
/// Examples:
/// <list type="bullet">
///   <item><description><c>2024/01/15/GET/200/3fa85f64-5717-4562-b3fc-2c963f66afa6.bin</c></description></item>
///   <item><description><c>transactions/2024/01/15/POST/201/3fa85f64-5717-4562-b3fc-2c963f66afa6.gz</c></description></item>
///   <item><description><c>prod/api/2024/01/15/DELETE/404/3fa85f64.bin</c></description></item>
/// </list>
/// The parser scans <em>right-to-left</em> through the slash-delimited segments,
/// which means any number of arbitrary prefix segments before <c>yyyy/MM/dd</c>
/// are tolerated without any configuration change.
/// </para>
///
/// <para>
/// <strong>Segment layout (right-to-left from filename):</strong>
/// <list type="number">
///   <item><description>Filename segment (contains the object UUID/ID and extension — ignored for metadata).</description></item>
///   <item><description>HTTP status code (e.g. <c>200</c>).</description></item>
///   <item><description>HTTP method (e.g. <c>GET</c>).</description></item>
///   <item><description>Day component of the capture date (<c>dd</c>).</description></item>
///   <item><description>Month component (<c>MM</c>).</description></item>
///   <item><description>Year component (<c>yyyy</c>).</description></item>
/// </list>
/// Any segment that is absent, or whose value cannot be parsed, produces a
/// sensible default with a warning log instead of an exception.
/// </para>
///
/// <para>
/// <strong>No I/O:</strong>
/// This extractor is a pure parsing component with no S3 or database dependencies.
/// </para>
/// </remarks>
internal sealed class PathEncodedMetadataExtractor : IMetadataExtractor
{
    // ── Defaults used when a path segment is missing or unparseable ───────────

    /// <summary>
    /// Default HTTP method returned when the method segment is absent or blank.
    /// Clearly distinct from any real HTTP method so callers can detect it.
    /// </summary>
    private const string DefaultMethod = "UNKNOWN";

    /// <summary>
    /// Default status code returned when the status segment is missing or
    /// contains a non-numeric value.
    /// <c>0</c> is not a valid HTTP status code, so it acts as an unambiguous sentinel.
    /// </summary>
    private const int DefaultStatusCode = 0;

    /// <summary>
    /// Default URL path returned when a sidecar or full parse is not available.
    /// The path cannot be recovered from the key alone in this format.
    /// </summary>
    private const string DefaultUrlPath = "/";

    /// <summary>
    /// Sentinel timestamp used when the date encoded in the key cannot be parsed.
    /// <see cref="DateTimeOffset.MinValue"/> produces an unmistakably wrong timestamp
    /// rather than a subtly plausible fallback value.
    /// </summary>
    private static readonly DateTimeOffset DefaultTimestamp = DateTimeOffset.MinValue;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ILogger<PathEncodedMetadataExtractor> _logger;

    /// <summary>
    /// Initialises the extractor with its required logger dependency.
    /// </summary>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public PathEncodedMetadataExtractor(ILogger<PathEncodedMetadataExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IMetadataExtractor ────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Parsing is performed <em>defensively</em>: every segment is treated as
    /// optional. An unparseable or absent segment produces a warning log entry and
    /// falls back to the appropriate default value. The method never throws for
    /// bad data; it only throws <see cref="ArgumentException"/> for a
    /// <see langword="null"/> or empty <paramref name="s3Key"/>, which is a
    /// programming error rather than a data-quality issue.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="s3Key"/> is <see langword="null"/> or empty.
    /// </exception>
    public TransactionMetadata ExtractFromKey(string s3Key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s3Key);

        // Split the key into path segments. Normalise forward-slashes only;
        // S3 keys do not use backslashes as separators.
        var segments = s3Key.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // ── Right-to-left segment parsing ─────────────────────────────────────
        // Layout (0-indexed from the right):
        //   [0]  filename (e.g. "3fa85f64-5717-4562-b3fc-2c963f66afa6.bin")
        //   [1]  status   (e.g. "200")
        //   [2]  method   (e.g. "GET")
        //   [3]  day      (e.g. "15")
        //   [4]  month    (e.g. "01")
        //   [5]  year     (e.g. "2024")
        //   [6+] optional prefix segments (ignored)
        //
        // Using right-to-left indexing means an arbitrary number of prefix segments
        // before the date does not shift the date/method/status positions.
        int total = segments.Length;

        // Parse method (right index 2).
        var method = ParseMethod(s3Key, segments, total, rightIndex: 2);

        // Parse status code (right index 1).
        var statusCode = ParseStatusCode(s3Key, segments, total, rightIndex: 1);

        // Parse timestamp from year/month/day (right indices 5/4/3).
        var timestamp = ParseDate(s3Key, segments, total,
            yearRightIndex:  5,
            monthRightIndex: 4,
            dayRightIndex:   3);

        _logger.LogDebug(
            "PathEncodedMetadataExtractor: parsed key '{S3Key}' → Method={Method}, Status={Status}, Timestamp={Timestamp:O}",
            s3Key, method, statusCode, timestamp);

        return new TransactionMetadata(
            S3Key:                 s3Key,
            Method:                method,
            StatusCode:            statusCode,
            UrlPath:               DefaultUrlPath,   // not encoded in the path — set by caller or sidecar
            TimestampUtc:          timestamp,
            CompressedSizeBytes:   0,                // unknown until S3 listing provides it
            DecompressedSizeBytes: 0,                // unknown until the body is fetched
            S3LastModified:        DefaultTimestamp  // unknown until S3 listing provides it
        );
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The sidecar JSON is deserialised preferentially for all fields. Any field
    /// absent from the sidecar (or that fails to parse) falls back to key-based
    /// extraction via <see cref="ExtractFromKey"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="s3Key"/> is <see langword="null"/> or empty,
    /// or when <paramref name="sidecarBytes"/> is <see langword="null"/> or empty.
    /// </exception>
    public TransactionMetadata ExtractFromSidecar(string s3Key, byte[] sidecarBytes)
    {
        // ASSUMPTION: PathEncodedMetadataExtractor.ExtractFromSidecar() is not the
        // primary use case for this extractor — sidecar parsing belongs to
        // SidecarMetadataExtractor. However, IMetadataExtractor requires both methods
        // be implemented. Here we fall back gracefully to key-based extraction plus a
        // warning so operators know they're calling the wrong extractor for this path.
        ArgumentException.ThrowIfNullOrWhiteSpace(s3Key);
        ArgumentNullException.ThrowIfNull(sidecarBytes);

        if (sidecarBytes.Length == 0)
        {
            throw new ArgumentException("Sidecar byte array must not be empty.", nameof(sidecarBytes));
        }

        _logger.LogWarning(
            "PathEncodedMetadataExtractor.ExtractFromSidecar() was called for key '{S3Key}'. " +
            "This extractor does not parse sidecar files; falling back to key-based extraction. " +
            "Consider configuring S3:MetadataExtractorStrategy = 'sidecar' if sidecar data is available.",
            s3Key);

        return ExtractFromKey(s3Key);
    }

    // ── Private parsing helpers ───────────────────────────────────────────────

    /// <summary>
    /// Returns the HTTP method segment at the specified right-to-left index,
    /// or <see cref="DefaultMethod"/> if the segment is missing or blank.
    /// </summary>
    private string ParseMethod(
        string s3Key,
        string[] segments,
        int total,
        int rightIndex)
    {
        if (!TryGetSegmentFromRight(segments, total, rightIndex, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning(
                "PathEncodedMetadataExtractor: HTTP method segment (right index {RightIndex}) " +
                "is missing from S3 key '{S3Key}'. Defaulting to '{Default}'.",
                rightIndex, s3Key, DefaultMethod);

            return DefaultMethod;
        }

        // Normalise to uppercase: GET, POST, DELETE, etc.
        return raw.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Returns the HTTP status code segment at the specified right-to-left index,
    /// or <see cref="DefaultStatusCode"/> if the segment is missing or non-numeric.
    /// </summary>
    private int ParseStatusCode(
        string s3Key,
        string[] segments,
        int total,
        int rightIndex)
    {
        if (!TryGetSegmentFromRight(segments, total, rightIndex, out var raw))
        {
            _logger.LogWarning(
                "PathEncodedMetadataExtractor: HTTP status code segment (right index {RightIndex}) " +
                "is missing from S3 key '{S3Key}'. Defaulting to {Default}.",
                rightIndex, s3Key, DefaultStatusCode);

            return DefaultStatusCode;
        }

        // Status codes are three-digit integers in the range 100–999.
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var code) ||
            code is < 100 or > 999)
        {
            _logger.LogWarning(
                "PathEncodedMetadataExtractor: Cannot parse '{Raw}' as a valid HTTP status code " +
                "in S3 key '{S3Key}'. Defaulting to {Default}.",
                raw, s3Key, DefaultStatusCode);

            return DefaultStatusCode;
        }

        return code;
    }

    /// <summary>
    /// Assembles a <see cref="DateTimeOffset"/> from the year, month, and day segments
    /// at the specified right-to-left indices. Returns <see cref="DefaultTimestamp"/>
    /// when any component is missing or contains a non-numeric / out-of-range value.
    /// </summary>
    private DateTimeOffset ParseDate(
        string s3Key,
        string[] segments,
        int total,
        int yearRightIndex,
        int monthRightIndex,
        int dayRightIndex)
    {
        if (!TryGetSegmentFromRight(segments, total, yearRightIndex,  out var yearStr)  ||
            !TryGetSegmentFromRight(segments, total, monthRightIndex, out var monthStr) ||
            !TryGetSegmentFromRight(segments, total, dayRightIndex,   out var dayStr))
        {
            _logger.LogWarning(
                "PathEncodedMetadataExtractor: One or more date segments (yyyy/MM/dd) are missing " +
                "from S3 key '{S3Key}'. Defaulting timestamp to {Default:O}.",
                s3Key, DefaultTimestamp);

            return DefaultTimestamp;
        }

        // Parse each numeric component individually for a precise error message.
        if (!int.TryParse(yearStr,  NumberStyles.None, CultureInfo.InvariantCulture, out var year)  ||
            !int.TryParse(monthStr, NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(dayStr,   NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            _logger.LogWarning(
                "PathEncodedMetadataExtractor: Date segments '{Year}/{Month}/{Day}' in S3 key " +
                "'{S3Key}' contain non-numeric characters. Defaulting timestamp to {Default:O}.",
                yearStr, monthStr, dayStr, s3Key, DefaultTimestamp);

            return DefaultTimestamp;
        }

        // Guard against values that would cause DateTime constructor to throw.
        // A corrupt key should never crash the listing pipeline.
        if (!IsValidDateComponents(year, month, day))
        {
            _logger.LogWarning(
                "PathEncodedMetadataExtractor: Date components {Year:D4}/{Month:D2}/{Day:D2} " +
                "parsed from S3 key '{S3Key}' are out of calendar range. " +
                "Defaulting timestamp to {Default:O}.",
                year, month, day, s3Key, DefaultTimestamp);

            return DefaultTimestamp;
        }

        // Construct as UTC midnight on the capture date. The time component cannot
        // be recovered from a path-only key; callers may refine it from sidecar data
        // or S3 object metadata (LastModified).
        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
    }

    // ── Low-level helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to retrieve the segment at position
    /// <c>total - 1 - rightIndex</c> from <paramref name="segments"/>.
    /// </summary>
    /// <param name="segments">The split key segments.</param>
    /// <param name="total">Total number of segments (avoids repeated <c>.Length</c> calls).</param>
    /// <param name="rightIndex">Zero-based offset from the rightmost segment.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, contains the trimmed segment value;
    /// otherwise contains <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the segment exists and is non-empty after trimming;
    /// <see langword="false"/> otherwise.
    /// </returns>
    private static bool TryGetSegmentFromRight(
        string[] segments,
        int total,
        int rightIndex,
        out string value)
    {
        var leftIndex = total - 1 - rightIndex;

        if (leftIndex < 0 || leftIndex >= total)
        {
            value = string.Empty;
            return false;
        }

        value = segments[leftIndex].Trim();
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the supplied year/month/day values
    /// can be passed to the <see cref="DateTime"/> constructor without throwing.
    /// </summary>
    private static bool IsValidDateComponents(int year, int month, int day)
    {
        if (year is < 1 or > 9999)   return false;
        if (month is < 1 or > 12)    return false;
        if (day is < 1 or > 31)      return false;

        // Final range check accounting for month length and leap years.
        return day <= DateTime.DaysInMonth(year, month);
    }
}
