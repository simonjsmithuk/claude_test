namespace DataViewer.Application.Services;

using System.IO.Compression;
using DataViewer.Application.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stateless gzip decompression service.
/// Detects gzip by magic bytes (<c>0x1F 0x8B</c> at offset 0) OR by the presence
/// of a <c>Content-Encoding: gzip</c> HTTP header value, then decompresses
/// on-the-fly via <see cref="GZipStream"/> without loading the full compressed
/// payload into a second buffer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Magic-byte detection:</strong>
/// RFC 1952 §2.3.1 defines the gzip magic number as the two-byte sequence
/// <c>0x1F 0x8B</c> at the very start of the stream. This is checked first as
/// the lowest-overhead signal. When the magic bytes are absent but a
/// <c>Content-Encoding: gzip</c> header is present, decompression is still
/// attempted — this handles edge cases where the leading bytes were rewritten
/// by an intermediate proxy.
/// </para>
/// <para>
/// <strong>Streaming decompression:</strong>
/// <see cref="GZipStream"/> wraps a <see cref="MemoryStream"/> view of the
/// input and writes decompressed chunks directly into the output
/// <see cref="MemoryStream"/> via <c>CopyToAsync</c>. The full compressed
/// payload is never duplicated in memory — only the output accumulates.
/// </para>
/// <para>
/// <strong>Registration:</strong>
/// Register as Singleton — the class holds no mutable state.
/// </para>
/// </remarks>
public sealed class GzipDecompressor : IGzipDecompressor
{
    // Gzip magic number per RFC 1952 §2.3.1.
    private const byte GzipMagicByte0 = 0x1F;
    private const byte GzipMagicByte1 = 0x8B;

    // Buffer size used by CopyToAsync for the streaming copy from GZipStream.
    // 81920 bytes (80 KB) is the .NET recommended default for I/O copy buffers.
    private const int CopyBufferSize = 81_920;

    private readonly ILogger<GzipDecompressor> _logger;

    /// <summary>
    /// Initialises the decompressor with its logger dependency.
    /// </summary>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public GzipDecompressor(ILogger<GzipDecompressor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Detection order:
    /// <list type="number">
    ///   <item><description>Magic bytes <c>0x1F 0x8B</c> at offset 0.</description></item>
    ///   <item><description><c>Content-Encoding</c> header equals <c>"gzip"</c> (case-insensitive).</description></item>
    /// </list>
    /// If neither signal is present the original <paramref name="data"/> array is returned
    /// unchanged with <c>WasCompressed = false</c>, avoiding any allocation.
    /// </remarks>
    public async Task<(byte[] Bytes, bool WasCompressed)> DecompressAsync(
        byte[] data,
        string? contentEncodingHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            _logger.LogDebug("GzipDecompressor: received empty byte array — returning as-is.");
            return (data, false);
        }

        bool isGzip = HasGzipMagicBytes(data) ||
                      IsGzipContentEncoding(contentEncodingHeader);

        if (!isGzip)
        {
            _logger.LogDebug(
                "GzipDecompressor: no gzip signal detected (magic bytes absent, "
                + "Content-Encoding='{ContentEncoding}') — returning original {Bytes} bytes unchanged.",
                contentEncodingHeader ?? "<null>",
                data.Length);

            return (data, false);
        }

        _logger.LogDebug(
            "GzipDecompressor: decompressing {CompressedBytes} bytes "
            + "(magic={HasMagic}, Content-Encoding='{ContentEncoding}').",
            data.Length,
            HasGzipMagicBytes(data),
            contentEncodingHeader ?? "<null>");

        var decompressed = await DecompressGzipAsync(data, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "GzipDecompressor: decompression complete — {CompressedBytes} → {DecompressedBytes} bytes.",
            data.Length,
            decompressed.Length);

        return (decompressed, true);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the first two bytes of <paramref name="data"/>
    /// match the gzip magic number (<c>0x1F 0x8B</c>) per RFC 1952 §2.3.1.
    /// </summary>
    private static bool HasGzipMagicBytes(byte[] data) =>
        data.Length >= 2 &&
        data[0] == GzipMagicByte0 &&
        data[1] == GzipMagicByte1;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="header"/> equals
    /// <c>"gzip"</c> using an ordinal case-insensitive comparison.
    /// Trims surrounding whitespace to handle malformed header values gracefully.
    /// </summary>
    private static bool IsGzipContentEncoding(string? header) =>
        !string.IsNullOrWhiteSpace(header) &&
        header.Trim().Equals("gzip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Streams decompression of <paramref name="compressed"/> via
    /// <see cref="GZipStream"/> wrapping an in-memory view, writing output chunks
    /// into a <see cref="MemoryStream"/> via <c>CopyToAsync</c>.
    /// The full compressed payload is never duplicated — only the output accumulates.
    /// </summary>
    /// <returns>The decompressed bytes as a new managed array.</returns>
    /// <exception cref="InvalidDataException">
    /// Propagated directly from <see cref="GZipStream"/> when the stream is corrupt
    /// or not a valid gzip payload.
    /// </exception>
    private static async Task<byte[]> DecompressGzipAsync(
        byte[] compressed,
        CancellationToken cancellationToken)
    {
        // MemoryStream wrapping the compressed bytes — zero-copy view, no duplication.
        using var inputStream = new MemoryStream(compressed, writable: false);

        // GZipStream reads from inputStream and decompresses on-the-fly.
        // leaveOpen: false — GZipStream disposes inputStream on its own disposal,
        // which is fine because inputStream does not own any unmanaged resources.
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress, leaveOpen: false);

        // Collect decompressed output into a MemoryStream so we can return a byte[].
        // No pre-allocated capacity: we don't know the decompressed size upfront.
        // ASSUMPTION: The caller (BodyTruncator) is responsible for enforcing the
        // size cap; GzipDecompressor decompresses the full stream without a ceiling.
        // If zip-bomb protection is needed at this layer, wrap the output stream
        // with a LimitedStream before calling CopyToAsync.
        using var outputStream = new MemoryStream();

        await gzipStream.CopyToAsync(outputStream, CopyBufferSize, cancellationToken)
            .ConfigureAwait(false);

        return outputStream.ToArray();
    }
}
