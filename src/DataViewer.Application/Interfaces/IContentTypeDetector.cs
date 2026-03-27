namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Enums;

/// <summary>
/// Detects the content type of a decoded body by inspecting its leading
/// non-whitespace bytes, optionally consulting the HTTP <c>Content-Type</c> header.
/// </summary>
/// <remarks>
/// Detection is deliberately shallow — only the first non-whitespace character (or
/// a short leading prefix) is examined to keep the hot path allocation-free. This
/// is sufficient for the DataViewer use-case where body content type is almost
/// always unambiguously signalled by the opening characters.
///
/// <para>
/// Implementations are stateless and must be safe for concurrent use from multiple
/// threads (Singleton lifetime).
/// </para>
/// </remarks>
public interface IContentTypeDetector
{
    /// <summary>
    /// Returns the <see cref="BodyContentType"/> for the supplied body bytes,
    /// optionally using the <c>Content-Type</c> header as the primary signal.
    /// </summary>
    /// <param name="bodyBytes">
    /// The raw decoded body bytes to inspect. May be <see langword="null"/> or empty;
    /// in that case <see cref="BodyContentType.Unknown"/> is returned.
    /// </param>
    /// <param name="contentTypeHeader">
    /// The raw <c>Content-Type</c> header value (e.g. <c>"application/json; charset=utf-8"</c>),
    /// or <see langword="null"/> when no header is available. When supplied,
    /// header-based detection takes precedence over byte sniffing.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="BodyContentType.Json"/> — first non-whitespace byte is
    ///       <c>{</c> or <c>[</c>, or the header contains <c>json</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="BodyContentType.Xml"/> — first non-whitespace bytes begin
    ///       with <c>&lt;?xml</c> or the first non-whitespace byte is <c>&lt;</c>,
    ///       or the header contains <c>xml</c> or <c>html</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="BodyContentType.Text"/> — all other non-empty bodies.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="BodyContentType.Unknown"/> — <paramref name="bodyBytes"/>
    ///       is <see langword="null"/> or empty.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    BodyContentType Detect(byte[]? bodyBytes, string? contentTypeHeader);
}
