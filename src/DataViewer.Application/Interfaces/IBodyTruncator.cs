namespace DataViewer.Application.Interfaces;

/// <summary>
/// Reads up to the configured size cap from a stream and reports whether the
/// stream contained more data beyond the cap (i.e. whether the result is truncated).
/// </summary>
/// <remarks>
/// <para>
/// The size cap is sourced from <see cref="DataViewer.Domain.Entities.SystemSettings.BodySizeCapMb"/>
/// via <see cref="ISystemSettingsRepository"/> at call time, so that administrative
/// changes to the cap take effect without an application restart.
/// </para>
/// <para>
/// The default cap is 5 MB when the settings row cannot be retrieved from the database.
/// </para>
/// <para>
/// Implementations are stateless beyond their injected dependencies and must be
/// safe for concurrent use from multiple threads (Singleton lifetime).
/// </para>
/// </remarks>
public interface IBodyTruncator
{
    /// <summary>
    /// Reads up to <c>BodySizeCapMb</c> megabytes from <paramref name="bodyStream"/>
    /// and returns the bytes together with a flag indicating whether the stream
    /// held additional data beyond the cap.
    /// </summary>
    /// <param name="bodyStream">
    /// A readable, forward-only stream of the body bytes. The caller retains
    /// ownership of the stream and is responsible for disposal.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>Bytes</c> — up to <c>BodySizeCapMb</c> megabytes read from the stream.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>IsTruncated</c> — <see langword="true"/> when the stream contained
    ///       more data beyond the cap; <see langword="false"/> when the entire stream
    ///       was consumed within the cap.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    Task<(byte[] Bytes, bool IsTruncated)> TruncateIfNeededAsync(
        Stream bodyStream,
        CancellationToken cancellationToken = default);
}
