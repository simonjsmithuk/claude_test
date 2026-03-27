namespace DataViewer.Infrastructure.S3.Parsers;

using DataViewer.Application.Interfaces;
using DataViewer.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stub implementation of <see cref="ITransactionParser"/> for the sidecar
/// transaction format, where a small JSON sidecar file stored alongside the
/// main S3 object contains pre-computed metadata and structured request/response
/// fields.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠ Not yet implemented — tracked under OQ-01.</strong>
/// </para>
/// <para>
/// This class exists to satisfy the DI registration contract introduced in
/// TASK-016 so that the <c>TransactionFormat = "sidecar"</c> configuration value
/// resolves to a named service without compile-time errors.  Attempting to call
/// <see cref="Parse"/> at runtime will always throw
/// <see cref="NotImplementedException"/> with a message referencing OQ-01.
/// </para>
/// <para>
/// <strong>Activation:</strong>
/// Set <c>S3:TransactionFormat</c> to <c>"sidecar"</c> in appsettings.json (or an
/// environment-specific override) to route all parse operations to this parser.
/// Do not do this in production until OQ-01 is resolved.
/// </para>
/// <para>
/// <strong>OQ-01 implementation notes (for the future implementer):</strong>
/// The sidecar format is expected to work as follows:
/// <list type="bullet">
///   <item>
///     <description>
///       For each transaction file <c>transactions/{date}/{key}.gz</c> the
///       traffic capture agent also writes <c>transactions/{date}/{key}_meta.json</c>
///       containing a JSON object with <c>method</c>, <c>path</c>, <c>statusCode</c>,
///       <c>requestHeaders</c>, <c>responseHeaders</c>, and optional
///       <c>requestBodyRef</c> / <c>responseBodyRef</c> fields.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="Parse"/> would need to accept both the primary gzip bytes and
///       the pre-fetched sidecar bytes, or <see cref="ITransactionParser"/> would
///       need an overload that accepts an optional sidecar parameter.
///     </description>
///   </item>
///   <item>
///     <description>
///       The sidecar format eliminates the need to download and decompress the full
///       transaction file for metadata-only queries, which is the primary performance
///       motivation for OQ-01.
///     </description>
///   </item>
/// </list>
/// </para>
/// </remarks>
internal sealed class SidecarTransactionParser : ITransactionParser
{
    private readonly ILogger<SidecarTransactionParser> _logger;

    /// <summary>
    /// Initialises the stub parser with its logger dependency.
    /// </summary>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public SidecarTransactionParser(ILogger<SidecarTransactionParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    /// <param name="data">Ignored.</param>
    /// <returns>Never returns — always throws.</returns>
    /// <exception cref="NotImplementedException">
    /// Always thrown. The sidecar transaction parser is not yet implemented.
    /// See open question OQ-01 in the System Design Document for the intended
    /// design and implementation notes.
    /// </exception>
    public ParsedTransaction Parse(byte[] data)
    {
        _logger.LogError(
            "SidecarTransactionParser.Parse() was called but the sidecar format is not yet " +
            "implemented (OQ-01). Set S3:TransactionFormat to 'delimiter' in configuration " +
            "to use the production-ready delimiter parser.");

        throw new NotImplementedException(
            "The sidecar transaction parser (S3:TransactionFormat = 'sidecar') is not yet " +
            "implemented. This feature is tracked under open question OQ-01 in the System " +
            "Design Document. Until OQ-01 is resolved, set S3:TransactionFormat to 'delimiter' " +
            "in appsettings.json or via the DATAVIEWER__S3__TRANSACTIONFORMAT environment variable.");
    }
}
