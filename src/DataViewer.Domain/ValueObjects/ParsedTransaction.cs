namespace DataViewer.Domain.ValueObjects;

using DataViewer.Domain.Enums;

/// <summary>
/// Immutable value object representing the fully-parsed content of a single
/// HTTP transaction record retrieved from S3.
/// </summary>
/// <remarks>
/// <para>
/// A transaction record stored in S3 is a gzip-compressed file whose structure
/// encodes HTTP request/response headers and optional body payloads. Once the
/// Infrastructure layer decompresses and parses the file, the result is
/// represented as a <see cref="ParsedTransaction"/> and passed up through the
/// application layer to the API controller for serialisation.
/// </para>
/// <para>
/// Body strings are <see langword="null"/> when the corresponding section of the
/// record contains no body. <see cref="IsRequestBodyTruncated"/> and
/// <see cref="IsResponseBodyTruncated"/> indicate whether the stored body was
/// cut off at the capture-time size limit; the frontend should surface this to the user.
/// </para>
/// <para>
/// Header dictionaries are exposed as <see cref="IReadOnlyDictionary{TKey,TValue}"/>
/// to enforce the immutability contract of this domain value object. Downstream
/// consumers must not mutate the header collections.
/// </para>
/// <para>
/// This type is a <c>record</c> to ensure structural equality and immutability
/// — all properties are <c>init</c>-only by default on records.
/// </para>
/// </remarks>
/// <param name="RequestHeaders">
///   Read-only dictionary of HTTP request header names to their values, normalised
///   to lowercase keys. Empty dictionary when the record contains no request headers.
/// </param>
/// <param name="ResponseHeaders">
///   Read-only dictionary of HTTP response header names to their values, normalised
///   to lowercase keys. Empty dictionary when the record contains no response headers.
/// </param>
/// <param name="RequestBody">
///   Decoded text of the HTTP request body.
///   <see langword="null"/> when the transaction record contains no request body.
/// </param>
/// <param name="ResponseBody">
///   Decoded text of the HTTP response body.
///   <see langword="null"/> when the transaction record contains no response body.
/// </param>
/// <param name="RequestBodyContentType">
///   Detected content type of <paramref name="RequestBody"/>.
///   <see langword="null"/> when <paramref name="RequestBody"/> is <see langword="null"/>.
/// </param>
/// <param name="ResponseBodyContentType">
///   Detected content type of <paramref name="ResponseBody"/>.
///   <see langword="null"/> when <paramref name="ResponseBody"/> is <see langword="null"/>.
/// </param>
/// <param name="IsRequestBodyTruncated">
///   <see langword="true"/> when the request body stored in S3 was truncated at the
///   capture-time size limit and therefore does not represent the full original payload.
/// </param>
/// <param name="IsResponseBodyTruncated">
///   <see langword="true"/> when the response body stored in S3 was truncated at the
///   capture-time size limit and therefore does not represent the full original payload.
/// </param>
/// <param name="Metadata">
///   Lightweight metadata associated with this transaction record (key, method,
///   status code, URL path, timestamps, and sizes).
/// </param>
public record ParsedTransaction(
    IReadOnlyDictionary<string, string> RequestHeaders,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? RequestBody,
    string? ResponseBody,
    BodyContentType? RequestBodyContentType,
    BodyContentType? ResponseBodyContentType,
    bool IsRequestBodyTruncated,
    bool IsResponseBodyTruncated,
    TransactionMetadata Metadata
);
