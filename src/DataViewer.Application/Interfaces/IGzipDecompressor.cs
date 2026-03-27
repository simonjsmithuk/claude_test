namespace DataViewer.Application.Interfaces;

/// <summary>
/// Decompresses gzip-encoded byte streams, detecting compression either by
/// the gzip magic bytes (<c>0x1F 0x8B</c>) at offset 0 or by a
/// <c>Content-Encoding: gzip</c> header value.
/// </summary>
/// <remarks>
/// <para>
/// The service is intentionally narrow: its sole responsibility is decompression.
/// Content-type detection and body truncation are handled by
/// <see cref="IContentTypeDetector"/> and <see cref="IBodyTruncator"/> respectively.
/// </para>
/// <para>
/// Implementations are stateless and must be safe for concurrent use from multiple
/// threads (Singleton lifetime).
/// </para>
/// </remarks>
public interface IGzipDecompressor
{
    /// <summary>
    /// Inspects <paramref name="data"/> to determine whether it is gzip-compressed,
    /// decompresses it if so, and returns both the resulting bytes and a flag
    /// indicating whether decompression occurred.
    /// </summary>
    /// <param name="data">
    /// The raw byte payload to inspect. Must not be <see langword="null"/>.
    /// An empty array is a valid input and always returns
    /// <c>(Array.Empty&lt;byte&gt;(), false)</c>.
    /// </param>
    /// <param name="contentEncodingHeader">
    /// The value of the HTTP <c>Content-Encoding</c> header (e.g.
    /// <c>"gzip"</c>, <c>"identity"</c>), or <see langword="null"/> when no
    /// such header is present. When this value equals <c>"gzip"</c>
    /// (case-insensitive) decompression is attempted regardless of whether the
    /// magic bytes are present, supporting edge cases where the leading bytes
    /// were altered in transit.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>Bytes</c> — the decompressed bytes when <c>WasCompressed</c> is
    ///       <see langword="true"/>; otherwise the original <paramref name="data"/>
    ///       reference unchanged.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>WasCompressed</c> — <see langword="true"/> when the input was
    ///       identified as gzip and decompressed; <see langword="false"/> when the
    ///       input was returned as-is.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Propagated when <paramref name="data"/> is identified as gzip (by magic bytes
    /// or header) but the stream cannot be decompressed (corrupt or truncated gzip).
    /// </exception>
    Task<(byte[] Bytes, bool WasCompressed)> DecompressAsync(
        byte[] data,
        string? contentEncodingHeader,
        CancellationToken cancellationToken = default);
}
