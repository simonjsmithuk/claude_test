namespace DataViewer.Infrastructure.S3.Parsers;

using DataViewer.Domain.Enums;

/// <summary>
/// Lightweight, allocation-minimal heuristic detector that classifies a UTF-8
/// body string as <see cref="BodyContentType.Json"/>, <see cref="BodyContentType.Xml"/>,
/// or <see cref="BodyContentType.Text"/> based on leading characters.
/// </summary>
/// <remarks>
/// <para>
/// Detection is intentionally shallow: it looks only at the first non-whitespace
/// character (or the first few characters) to avoid scanning the entire body for
/// every request. This is sufficient for the DataViewer use-case where the content
/// type is almost always clearly signalled by the opening bytes.
/// </para>
/// <para>
/// When a <c>Content-Type</c> header is available the caller should prefer header-based
/// detection; this helper is used as a fallback when the header is missing or
/// ambiguous (e.g. <c>application/octet-stream</c>).
/// </para>
/// </remarks>
internal static class BodyContentTypeDetector
{
    /// <summary>
    /// Detects the content type of a body string, optionally consulting the
    /// <c>Content-Type</c> header value first.
    /// </summary>
    /// <param name="body">
    /// The decoded body text. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="contentTypeHeader">
    /// The raw <c>Content-Type</c> header value (e.g. <c>"application/json; charset=utf-8"</c>),
    /// or <see langword="null"/> when no header is available.
    /// </param>
    /// <returns>
    /// The detected <see cref="BodyContentType"/>; never <see cref="BodyContentType.Unknown"/>
    /// when a non-empty body is supplied (falls back to <see cref="BodyContentType.Text"/>).
    /// Returns <see cref="BodyContentType.Unknown"/> only when <paramref name="body"/> is
    /// <see langword="null"/> or empty.
    /// </returns>
    internal static BodyContentType Detect(string? body, string? contentTypeHeader)
    {
        if (string.IsNullOrEmpty(body))
        {
            return BodyContentType.Unknown;
        }

        // ── Step 1: header-based detection (fast path) ───────────────────────
        if (!string.IsNullOrEmpty(contentTypeHeader))
        {
            var header = contentTypeHeader.AsSpan();
            if (ContainsIgnoreCase(header, "json"))
            {
                return BodyContentType.Json;
            }

            if (ContainsIgnoreCase(header, "xml") ||
                ContainsIgnoreCase(header, "html"))
            {
                return BodyContentType.Xml;
            }
        }

        // ── Step 2: heuristic sniff on the first non-whitespace byte ─────────
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.IsEmpty)
        {
            return BodyContentType.Text;
        }

        var first = trimmed[0];

        if (first is '{' or '[')
        {
            return BodyContentType.Json;
        }

        if (first == '<')
        {
            return BodyContentType.Xml;
        }

        return BodyContentType.Text;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="source"/> contains
    /// <paramref name="value"/> using an ordinal case-insensitive comparison.
    /// Uses <see cref="MemoryExtensions.Contains{T}"/> on the span to avoid
    /// allocating a lower-cased string.
    /// </summary>
    private static bool ContainsIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
