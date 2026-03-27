using System.IO.Compression;
using System.Text;

namespace DataViewer.Tests.S3.Parsers;

/// <summary>
/// Fluent builder that constructs raw gzip-compressed transaction payloads in the
/// delimiter format understood by <c>DelimiterTransactionParser</c>.
/// </summary>
/// <remarks>
/// Separates test-data construction concerns from assertion logic, keeping
/// individual test methods concise and readable.
/// </remarks>
internal sealed class TransactionPayloadBuilder
{
    // ── Request fields ────────────────────────────────────────────────────────

    private string _requestMarker  = "--- REQUEST ---";
    private string _method         = "GET";
    private string _requestPath    = "/api/orders/42";
    private string _httpVersion    = "HTTP/1.1";
    private readonly Dictionary<string, string> _requestHeaders = new();
    private string? _requestBody   = null;

    // ── Response fields ───────────────────────────────────────────────────────

    private string _responseMarker = "--- RESPONSE ---";
    private int    _statusCode     = 200;
    private string _reasonPhrase   = "OK";
    private string _responseHttpVersion = "HTTP/1.1";
    private readonly Dictionary<string, string> _responseHeaders = new();
    private string? _responseBody  = null;

    // ── Line-ending control ───────────────────────────────────────────────────

    private string _lineEnding     = "\n";          // bare LF by default; \r\n for CRLF tests

    // ── Fluent setters ────────────────────────────────────────────────────────

    internal TransactionPayloadBuilder WithMethod(string method)
        { _method = method; return this; }

    internal TransactionPayloadBuilder WithRequestPath(string path)
        { _requestPath = path; return this; }

    internal TransactionPayloadBuilder WithHttpVersion(string version)
        { _httpVersion = version; return this; }

    internal TransactionPayloadBuilder WithRequestHeader(string name, string value)
        { _requestHeaders[name] = value; return this; }

    internal TransactionPayloadBuilder WithRequestBody(string body)
        { _requestBody = body; return this; }

    internal TransactionPayloadBuilder WithStatusCode(int code, string reason = "")
        { _statusCode = code; _reasonPhrase = reason; return this; }

    internal TransactionPayloadBuilder WithResponseHeader(string name, string value)
        { _responseHeaders[name] = value; return this; }

    internal TransactionPayloadBuilder WithResponseBody(string body)
        { _responseBody = body; return this; }

    internal TransactionPayloadBuilder WithCrlfLineEndings()
        { _lineEnding = "\r\n"; return this; }

    internal TransactionPayloadBuilder WithLfLineEndings()
        { _lineEnding = "\n"; return this; }

    // ── Marker manipulation (for negative tests) ──────────────────────────────

    internal TransactionPayloadBuilder WithRequestMarker(string marker)
        { _requestMarker = marker; return this; }

    internal TransactionPayloadBuilder WithResponseMarker(string marker)
        { _responseMarker = marker; return this; }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the complete raw text, then gzip-compresses it, and returns the
    /// compressed bytes — exactly what <c>DelimiterTransactionParser.Parse()</c> expects.
    /// </summary>
    internal byte[] BuildGzipped()
    {
        var text = BuildRawText();
        return GzipCompress(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Returns the uncompressed UTF-8 text of the built payload — useful for
    /// inspecting the exact bytes being fed to the parser.
    /// </summary>
    internal string BuildRawText()
    {
        var sb = new StringBuilder();
        var le = _lineEnding;

        // ── REQUEST section ───────────────────────────────────────────────────
        sb.Append(_requestMarker).Append(le);
        sb.Append(_method).Append(' ').Append(_requestPath).Append(' ').Append(_httpVersion).Append(le);

        foreach (var (name, value) in _requestHeaders)
        {
            sb.Append(name).Append(": ").Append(value).Append(le);
        }

        sb.Append(le); // blank line separating headers from body

        if (_requestBody is not null)
        {
            sb.Append(_requestBody).Append(le);
        }

        // ── RESPONSE section ──────────────────────────────────────────────────
        sb.Append(_responseMarker).Append(le);

        var reasonStr = string.IsNullOrWhiteSpace(_reasonPhrase)
            ? string.Empty
            : " " + _reasonPhrase;
        sb.Append(_responseHttpVersion).Append(' ')
          .Append(_statusCode).Append(reasonStr).Append(le);

        foreach (var (name, value) in _responseHeaders)
        {
            sb.Append(name).Append(": ").Append(value).Append(le);
        }

        sb.Append(le); // blank line separating headers from body

        if (_responseBody is not null)
        {
            sb.Append(_responseBody).Append(le);
        }

        return sb.ToString();
    }

    // ── Static convenience factories ──────────────────────────────────────────

    /// <summary>Returns a builder pre-configured with sensible defaults.</summary>
    internal static TransactionPayloadBuilder AValid() => new();

    /// <summary>
    /// Compresses <paramref name="data"/> using gzip and returns the compressed bytes.
    /// </summary>
    internal static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /// <summary>Returns gzip-compressed bytes of the given plain UTF-8 text.</summary>
    internal static byte[] GzipText(string text)
        => GzipCompress(Encoding.UTF8.GetBytes(text));
}
