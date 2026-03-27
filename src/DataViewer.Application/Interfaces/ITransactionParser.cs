namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.ValueObjects;

/// <summary>
/// Parses the raw byte content of an S3 transaction file into a structured
/// <see cref="ParsedTransaction"/> value object.
/// </summary>
/// <remarks>
/// Each transaction record stored in S3 is a gzip-compressed binary file whose
/// structure encodes HTTP request/response headers and optional body payloads.
/// The infrastructure implementation handles decompression and binary format parsing,
/// returning a fully-typed <see cref="ParsedTransaction"/> to the application layer.
///
/// <para>
/// Parsing is intentionally synchronous: all work is CPU-bound, in-memory byte
/// manipulation for which async overhead would add latency without benefit.
/// Callers that receive bytes from an async I/O source (e.g. S3 download) should
/// invoke <see cref="Parse"/> on a background thread if needed rather than making
/// this interface async.
/// </para>
///
/// <para>
/// <see cref="Parse"/> must not throw for corrupted or truncated records —
/// partial results should be returned with the truncation flags set on
/// <see cref="ParsedTransaction"/> where applicable. Only unrecoverable format
/// violations (e.g. bytes that are not a valid gzip stream) should propagate as
/// exceptions.
/// </para>
/// </remarks>
public interface ITransactionParser
{
    /// <summary>
    /// Parses the supplied raw bytes of an S3 transaction file.
    /// </summary>
    /// <param name="data">
    /// The raw (gzip-compressed) byte content of the S3 object, as returned by
    /// <see cref="IS3Service.GetObjectAsync"/>. Must not be <see langword="null"/>
    /// or empty; callers should validate length before invoking this method.
    /// </param>
    /// <returns>
    /// A fully-populated <see cref="ParsedTransaction"/> containing the parsed HTTP
    /// headers, optional body strings, content-type classifications, truncation flags,
    /// and associated <see cref="TransactionMetadata"/>.
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.DomainException">
    /// Thrown when the supplied bytes cannot be interpreted as a valid transaction
    /// record at all (e.g. not gzip-compressed, unknown binary format).
    /// Partial parse failures (e.g. truncated body) must be indicated via the
    /// truncation flags on <see cref="ParsedTransaction"/> rather than exceptions.
    /// </exception>
    ParsedTransaction Parse(byte[] data);
}
