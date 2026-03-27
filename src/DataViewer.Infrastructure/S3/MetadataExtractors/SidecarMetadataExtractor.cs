namespace DataViewer.Infrastructure.S3.MetadataExtractors;

using System.Text.Json;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Extracts <see cref="TransactionMetadata"/> by deserialising a JSON sidecar file
/// stored alongside the primary S3 transaction object.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Sidecar contract:</strong>
/// For each primary transaction file the capture agent also writes a small JSON
/// companion file (conventionally named <c>{key}_meta.json</c>) containing
/// pre-computed metadata. <see cref="ExtractFromSidecar"/> deserialises this JSON
/// directly, which is faster than downloading and decompressing the full transaction
/// file for metadata-only listing operations.
/// </para>
///
/// <para>
/// <strong>Field merging:</strong>
/// Fields that are absent from the sidecar JSON (or that fail to parse) fall back
/// to their default values rather than throwing. No field in the sidecar is
/// treated as mandatory, because older sidecar files written before a field was
/// introduced must still deserialise successfully.
/// </para>
///
/// <para>
/// <strong>ExtractFromKey — not implemented (OQ-02):</strong>
/// <see cref="ExtractFromKey"/> always throws <see cref="NotImplementedException"/>.
/// Sidecar-strategy deployments obtain metadata exclusively from the sidecar file;
/// they do not encode metadata in the key path. Key-based extraction for the sidecar
/// strategy is tracked under open question OQ-02 in the System Design Document.
/// Set <c>S3:MetadataExtractorStrategy</c> to <c>"path"</c> to use
/// <see cref="PathEncodedMetadataExtractor"/> instead.
/// </para>
///
/// <para>
/// <strong>No I/O:</strong>
/// This extractor is a pure parsing component with no S3 or database dependencies.
/// </para>
/// </remarks>
internal sealed class SidecarMetadataExtractor : IMetadataExtractor
{
    // ── Defaults used when sidecar fields are absent or unparseable ───────────

    /// <summary>
    /// Default HTTP method used when the sidecar does not include a <c>method</c> field.
    /// </summary>
    private const string DefaultMethod = "UNKNOWN";

    /// <summary>
    /// Default status code used when the sidecar does not include a valid
    /// <c>statusCode</c> field. <c>0</c> is not a valid HTTP status code and acts
    /// as an unambiguous sentinel.
    /// </summary>
    private const int DefaultStatusCode = 0;

    /// <summary>
    /// Default URL path used when the sidecar does not include a <c>urlPath</c> field.
    /// </summary>
    private const string DefaultUrlPath = "/";

    /// <summary>
    /// Sentinel timestamp used when <c>timestampUtc</c> is absent or cannot be parsed.
    /// <see cref="DateTimeOffset.MinValue"/> is unmistakably wrong rather than
    /// subtly plausible.
    /// </summary>
    private static readonly DateTimeOffset DefaultTimestamp = DateTimeOffset.MinValue;

    // ── JSON deserialisation options ──────────────────────────────────────────

    /// <summary>
    /// Shared, thread-safe <see cref="JsonSerializerOptions"/> for sidecar parsing.
    /// <list type="bullet">
    ///   <item><description>Property name matching is case-insensitive to tolerate
    ///   minor case variations between capture agent versions.</description></item>
    ///   <item><description>Unknown JSON properties are silently ignored, ensuring
    ///   forward-compatibility when the capture agent introduces new fields.</description></item>
    /// </list>
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // System.Text.Json ignores unknown properties by default in .NET 8;
        // setting this explicitly documents the intent and guards against future
        // changes to the default behaviour.
        UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ILogger<SidecarMetadataExtractor> _logger;

    /// <summary>
    /// Initialises the extractor with its required logger dependency.
    /// </summary>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public SidecarMetadataExtractor(ILogger<SidecarMetadataExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IMetadataExtractor ────────────────────────────────────────────────────

    /// <summary>
    /// Not implemented for the sidecar strategy.
    /// </summary>
    /// <param name="s3Key">Ignored.</param>
    /// <returns>Never returns — always throws.</returns>
    /// <exception cref="NotImplementedException">
    /// Always thrown. The sidecar metadata extractor does not support key-based
    /// extraction because sidecar-strategy deployments do not encode metadata in
    /// the S3 key path. This feature is tracked under open question OQ-02 in the
    /// System Design Document.
    /// Set <c>S3:MetadataExtractorStrategy</c> to <c>"path"</c> to use
    /// <see cref="PathEncodedMetadataExtractor.ExtractFromKey"/> instead.
    /// </exception>
    public TransactionMetadata ExtractFromKey(string s3Key)
    {
        _logger.LogError(
            "SidecarMetadataExtractor.ExtractFromKey() was called for key '{S3Key}' but " +
            "key-based extraction is not implemented for the sidecar strategy (OQ-02). " +
            "Set S3:MetadataExtractorStrategy to 'path' in configuration to use " +
            "PathEncodedMetadataExtractor instead.",
            s3Key);

        // ASSUMPTION: ExtractFromKey on the sidecar extractor is deliberately a stub.
        // Sidecar-strategy installations always have the sidecar JSON available and
        // never fall back to key-only extraction. OQ-02 covers the design work required
        // to define a key convention for sidecar deployments if that ever becomes necessary.
        throw new NotImplementedException(
            "SidecarMetadataExtractor.ExtractFromKey() is not yet implemented. " +
            "The sidecar metadata strategy does not encode metadata in the S3 key path. " +
            "This is tracked under open question OQ-02 in the System Design Document. " +
            "Until OQ-02 is resolved, set S3:MetadataExtractorStrategy to 'path' in " +
            "appsettings.json or via the DATAVIEWER__S3__METADATAEXTRACTORSTRATEGY " +
            "environment variable.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Each field in the sidecar is treated as optional and parsed defensively.
    /// When a field is absent, <see langword="null"/>, or contains a value that
    /// cannot be interpreted (e.g. a status code outside <c>100–999</c>), a warning
    /// is logged and the appropriate default value is used instead of throwing.
    /// </para>
    /// <para>
    /// If the <paramref name="sidecarBytes"/> cannot be deserialised as JSON at all
    /// (e.g. the bytes are binary garbage), a warning is logged and all fields fall
    /// back to their defaults. This guarantees the listing pipeline continues even
    /// when a single sidecar file is corrupt.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="s3Key"/> is <see langword="null"/> or empty,
    /// or when <paramref name="sidecarBytes"/> is <see langword="null"/> or empty.
    /// </exception>
    public TransactionMetadata ExtractFromSidecar(string s3Key, byte[] sidecarBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s3Key);
        ArgumentNullException.ThrowIfNull(sidecarBytes);

        if (sidecarBytes.Length == 0)
        {
            throw new ArgumentException("Sidecar byte array must not be empty.", nameof(sidecarBytes));
        }

        // ── Step 1: Deserialise ───────────────────────────────────────────────
        SidecarDocument? doc = DeserialiseDocument(s3Key, sidecarBytes);

        // ── Step 2: Map each field defensively ───────────────────────────────
        var method     = ResolveMethod(s3Key, doc);
        var statusCode = ResolveStatusCode(s3Key, doc);
        var urlPath    = ResolveUrlPath(s3Key, doc);
        var timestamp  = ResolveTimestamp(s3Key, doc);
        var compressed = doc?.CompressedSizeBytes ?? 0L;
        var expanded   = doc?.DecompressedSizeBytes ?? 0L;

        _logger.LogDebug(
            "SidecarMetadataExtractor: deserialised sidecar for key '{S3Key}' → " +
            "Method={Method}, Status={Status}, UrlPath='{UrlPath}', Timestamp={Timestamp:O}",
            s3Key, method, statusCode, urlPath, timestamp);

        return new TransactionMetadata(
            S3Key:                 s3Key,
            Method:                method,
            StatusCode:            statusCode,
            UrlPath:               urlPath,
            TimestampUtc:          timestamp,
            CompressedSizeBytes:   compressed,
            DecompressedSizeBytes: expanded,
            S3LastModified:        DefaultTimestamp  // not stored in sidecar; overlaid by S3 listing
        );
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to deserialise <paramref name="sidecarBytes"/> as a
    /// <see cref="SidecarDocument"/>. Returns <see langword="null"/> on any
    /// JSON parse error and logs a warning, so the caller's field-level defaults
    /// still apply.
    /// </summary>
    private SidecarDocument? DeserialiseDocument(string s3Key, byte[] sidecarBytes)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<SidecarDocument>(sidecarBytes, JsonOptions);

            if (doc is null)
            {
                _logger.LogWarning(
                    "SidecarMetadataExtractor: Sidecar JSON for key '{S3Key}' deserialised as null " +
                    "(JSON token was 'null' or the document was empty). All metadata fields will use defaults.",
                    s3Key);
            }

            return doc;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "SidecarMetadataExtractor: Failed to deserialise sidecar JSON for key '{S3Key}'. " +
                "The file may be corrupt or not valid JSON. All metadata fields will use defaults.",
                s3Key);

            return null;
        }
    }

    /// <summary>
    /// Resolves the HTTP method from the sidecar document, falling back to
    /// <see cref="DefaultMethod"/> with a warning when the field is absent or blank.
    /// </summary>
    private string ResolveMethod(string s3Key, SidecarDocument? doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.Method))
        {
            _logger.LogWarning(
                "SidecarMetadataExtractor: Sidecar for key '{S3Key}' is missing the 'method' field. " +
                "Defaulting to '{Default}'.",
                s3Key, DefaultMethod);

            return DefaultMethod;
        }

        return doc.Method.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Resolves the HTTP status code from the sidecar document, falling back to
    /// <see cref="DefaultStatusCode"/> with a warning when the field is absent or
    /// outside the valid range.
    /// </summary>
    private int ResolveStatusCode(string s3Key, SidecarDocument? doc)
    {
        if (doc?.StatusCode is null)
        {
            _logger.LogWarning(
                "SidecarMetadataExtractor: Sidecar for key '{S3Key}' is missing the 'statusCode' field. " +
                "Defaulting to {Default}.",
                s3Key, DefaultStatusCode);

            return DefaultStatusCode;
        }

        var code = doc.StatusCode.Value;

        if (code is < 100 or > 999)
        {
            _logger.LogWarning(
                "SidecarMetadataExtractor: Sidecar for key '{S3Key}' contains status code {Code} " +
                "which is outside the valid HTTP range 100–999. Defaulting to {Default}.",
                s3Key, code, DefaultStatusCode);

            return DefaultStatusCode;
        }

        return code;
    }

    /// <summary>
    /// Resolves the URL path from the sidecar document, falling back to
    /// <see cref="DefaultUrlPath"/> with a warning when the field is absent or blank.
    /// </summary>
    private string ResolveUrlPath(string s3Key, SidecarDocument? doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.UrlPath))
        {
            _logger.LogWarning(
                "SidecarMetadataExtractor: Sidecar for key '{S3Key}' is missing the 'urlPath' field. " +
                "Defaulting to '{Default}'.",
                s3Key, DefaultUrlPath);

            return DefaultUrlPath;
        }

        return doc.UrlPath.Trim();
    }

    /// <summary>
    /// Resolves the UTC capture timestamp from the sidecar document, falling back to
    /// <see cref="DefaultTimestamp"/> with a warning when the field is absent or
    /// cannot be interpreted as a valid UTC timestamp.
    /// </summary>
    private DateTimeOffset ResolveTimestamp(string s3Key, SidecarDocument? doc)
    {
        if (doc?.TimestampUtc is null)
        {
            _logger.LogWarning(
                "SidecarMetadataExtractor: Sidecar for key '{S3Key}' is missing the 'timestampUtc' field. " +
                "Defaulting to {Default:O}.",
                s3Key, DefaultTimestamp);

            return DefaultTimestamp;
        }

        var ts = doc.TimestampUtc.Value;

        // Normalise to UTC offset to match TransactionMetadata contract.
        // A sidecar written with a non-UTC offset is converted rather than rejected,
        // because conversion is lossless.
        var utc = ts.ToUniversalTime();

        if (utc == DateTimeOffset.MinValue)
        {
            _logger.LogWarning(
                "SidecarMetadataExtractor: Sidecar timestamp for key '{S3Key}' resolved to " +
                "DateTimeOffset.MinValue after UTC conversion, which indicates an invalid original value. " +
                "Defaulting to {Default:O}.",
                s3Key, DefaultTimestamp);

            return DefaultTimestamp;
        }

        return utc;
    }
}
