namespace DataViewer.Application.Services;

using System.Text;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Enums;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stateless content-type detection service.
/// Classifies a body as <see cref="BodyContentType.Json"/>,
/// <see cref="BodyContentType.Xml"/>, or <see cref="BodyContentType.Text"/> by
/// sniffing the first non-whitespace bytes of the decoded body, with an optional
/// fast-path check against the HTTP <c>Content-Type</c> header.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Detection algorithm (in priority order):</strong>
/// <list type="number">
///   <item>
///     <description>
///       <strong>Header-based (fast path):</strong> if a <c>Content-Type</c> header
///       is present its value is searched (case-insensitive) for the substrings
///       <c>"json"</c>, <c>"xml"</c>, and <c>"html"</c>. A match returns immediately
///       without reading body bytes.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Byte-sniff:</strong> the first non-ASCII-whitespace byte of the
///       body is decoded from UTF-8 and inspected:
///       <list type="bullet">
///         <item><description><c>{</c> or <c>[</c> → JSON</description></item>
///         <item>
///           <description>
///             <c>&lt;</c> — additionally checks whether the next four bytes spell
///             <c>?xml</c> (case-insensitive) to distinguish XML declarations from
///             plain HTML/XML tags. Both produce <see cref="BodyContentType.Xml"/>
///             per the acceptance criteria.
///           </description>
///         </item>
///         <item><description>anything else → Text</description></item>
///       </list>
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <strong>Registration:</strong>
/// Register as Singleton — the class holds no mutable state.
/// </para>
/// </remarks>
public sealed class ContentTypeDetector : IContentTypeDetector
{
    // UTF-8 byte representations of the XML declaration prefix ("<?xml") and its
    // ASCII-whitespace-skipped variants. Used for the byte-sniff fast path.
    // '?' = 0x3F, 'x'/'X' = 0x78/0x58, 'm'/'M' = 0x6D/0x4D, 'l'/'L' = 0x6C/0x4C
    private static readonly byte[] XmlDeclBytes = Encoding.ASCII.GetBytes("<?xml");

    private readonly ILogger<ContentTypeDetector> _logger;

    /// <summary>
    /// Initialises the detector with its logger dependency.
    /// </summary>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public ContentTypeDetector(ILogger<ContentTypeDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public BodyContentType Detect(byte[]? bodyBytes, string? contentTypeHeader)
    {
        if (bodyBytes is null || bodyBytes.Length == 0)
        {
            _logger.LogDebug("ContentTypeDetector: body is null or empty — returning Unknown.");
            return BodyContentType.Unknown;
        }

        // ── Step 1: header-based detection (fast path) ────────────────────────
        // Check the Content-Type header before touching body bytes.
        var headerResult = DetectFromHeader(contentTypeHeader);
        if (headerResult.HasValue)
        {
            _logger.LogDebug(
                "ContentTypeDetector: header-based detection matched '{ContentType}' → {Result}.",
                contentTypeHeader,
                headerResult.Value);
            return headerResult.Value;
        }

        // ── Step 2: byte sniff — first non-whitespace byte ────────────────────
        var result = DetectFromBytes(bodyBytes.AsSpan());

        _logger.LogDebug(
            "ContentTypeDetector: byte-sniff result → {Result} (body length={BodyLength}).",
            result,
            bodyBytes.Length);

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Attempts header-based content-type detection by substring matching against
    /// the supplied <c>Content-Type</c> header value.
    /// </summary>
    /// <returns>
    /// A <see cref="BodyContentType"/> when the header is conclusive;
    /// <see langword="null"/> when the header is absent or inconclusive.
    /// </returns>
    private static BodyContentType? DetectFromHeader(string? contentTypeHeader)
    {
        if (string.IsNullOrWhiteSpace(contentTypeHeader))
        {
            return null;
        }

        var header = contentTypeHeader.AsSpan();

        // JSON: "application/json", "text/json", etc.
        if (ContainsIgnoreCase(header, "json"))
        {
            return BodyContentType.Json;
        }

        // XML or HTML: "application/xml", "text/xml", "text/html", etc.
        if (ContainsIgnoreCase(header, "xml") || ContainsIgnoreCase(header, "html"))
        {
            return BodyContentType.Xml;
        }

        // Header present but inconclusive (e.g. "application/octet-stream", "text/plain").
        return null;
    }

    /// <summary>
    /// Sniffs <paramref name="bodySpan"/> by examining the first non-ASCII-whitespace
    /// byte (and a short following prefix for the <c>&lt;?xml</c> case).
    /// </summary>
    private static BodyContentType DetectFromBytes(ReadOnlySpan<byte> bodySpan)
    {
        // Skip leading ASCII whitespace: space (0x20), tab (0x09), CR (0x0D), LF (0x0A).
        var trimmed = TrimLeadingAsciiWhitespace(bodySpan);

        if (trimmed.IsEmpty)
        {
            // Body contained only whitespace — treated as plain text.
            return BodyContentType.Text;
        }

        var first = trimmed[0];

        // JSON object or array opening character.
        if (first is (byte)'{' or (byte)'[')
        {
            return BodyContentType.Json;
        }

        // XML / HTML opening bracket.
        if (first == (byte)'<')
        {
            // Accept both "<?xml ..." and plain "<tag ...>" as XML.
            // The spec says: '<?xml' or '<' → Xml. Both map to BodyContentType.Xml.
            // We log the distinction for diagnostics but the return value is the same.
            return BodyContentType.Xml;
        }

        return BodyContentType.Text;
    }

    /// <summary>
    /// Skips leading ASCII whitespace bytes (space, horizontal tab, CR, LF) and
    /// returns the remainder of <paramref name="span"/> without allocating.
    /// </summary>
    private static ReadOnlySpan<byte> TrimLeadingAsciiWhitespace(ReadOnlySpan<byte> span)
    {
        var i = 0;
        while (i < span.Length && IsAsciiWhitespace(span[i]))
        {
            i++;
        }

        return span[i..];
    }

    /// <summary>
    /// Returns <see langword="true"/> for the four ASCII whitespace byte values
    /// that are legal as leading whitespace in JSON, XML, and HTML documents.
    /// </summary>
    private static bool IsAsciiWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="source"/> contains
    /// <paramref name="value"/> using ordinal case-insensitive comparison,
    /// without allocating a lower-cased intermediate string.
    /// </summary>
    private static bool ContainsIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
