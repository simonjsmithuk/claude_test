namespace DataViewer.Domain.ValueObjects;

/// <summary>
/// Immutable value object that captures the lightweight metadata for a single
/// HTTP transaction record stored in S3.
/// </summary>
/// <remarks>
/// <para>
/// Metadata is derived from the S3 object key (which encodes method, status code,
/// URL path, and timestamp by convention) together with S3 object attributes
/// (size, last-modified). It is used to populate search results without downloading
/// the full object body.
/// </para>
/// <para>
/// This type is a <c>record</c> to ensure structural equality, consistent hashing,
/// and immutability — all properties are <c>init</c>-only by default on records.
/// </para>
/// <para>
/// All timestamp parameters are <see cref="DateTimeOffset"/> to carry unambiguous
/// UTC context. The Infrastructure S3 parsing layer must ensure both
/// <see cref="TimestampUtc"/> and <see cref="S3LastModified"/> are constructed with
/// <c>DateTimeOffset.UtcNow</c> or a UTC-offset of <c>+00:00</c>.
/// </para>
/// </remarks>
/// <param name="S3Key">
///   The full S3 object key under which the transaction file is stored.
///   Serves as the unique identifier used to fetch the full record on demand.
/// </param>
/// <param name="Method">
///   HTTP method of the captured request (e.g. <c>GET</c>, <c>POST</c>).
///   Extracted from the S3 object key by the parsing layer.
/// </param>
/// <param name="StatusCode">
///   HTTP status code of the captured response (e.g. <c>200</c>, <c>404</c>).
///   Extracted from the S3 object key by the parsing layer.
/// </param>
/// <param name="UrlPath">
///   URL path component of the captured request (e.g. <c>/api/orders/42</c>).
///   Extracted from the S3 object key by the parsing layer.
/// </param>
/// <param name="TimestampUtc">
///   UTC timestamp at which the HTTP transaction was captured.
///   Extracted from the S3 object key by the parsing layer.
/// </param>
/// <param name="CompressedSizeBytes">
///   Size of the gzip-compressed S3 object in bytes, as reported by S3.
///   Used to give the user a sense of transfer cost before downloading.
/// </param>
/// <param name="DecompressedSizeBytes">
///   Size of the object in bytes after gzip decompression.
///   Populated once the body has been fetched and decompressed.
/// </param>
/// <param name="S3LastModified">
///   UTC timestamp of the most recent modification to the S3 object,
///   as reported by the S3 ListObjectsV2 / HeadObject response.
/// </param>
public record TransactionMetadata(
    string S3Key,
    string Method,
    int StatusCode,
    string UrlPath,
    DateTimeOffset TimestampUtc,
    long CompressedSizeBytes,
    long DecompressedSizeBytes,
    DateTimeOffset S3LastModified
);
