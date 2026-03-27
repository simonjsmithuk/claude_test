namespace DataViewer.Application.DTOs.Transactions;

/// <summary>
/// Lightweight summary of a single HTTP transaction record, returned in paged
/// search results from GET /api/transactions.
/// </summary>
/// <remarks>
/// This DTO is intentionally lightweight — it contains only the metadata extracted
/// from the S3 object key and object attributes, with no body content. Retrieving
/// the full transaction body requires a separate GET /api/transactions/{s3Key} call.
/// </remarks>
public sealed record TransactionSummaryDto
{
    /// <summary>
    /// The full S3 object key that uniquely identifies this transaction record.
    /// Used as the identifier in the GET /api/transactions/{s3Key} detail endpoint.
    /// </summary>
    public string S3Key { get; init; } = string.Empty;

    /// <summary>
    /// HTTP method of the captured request (e.g. <c>GET</c>, <c>POST</c>, <c>DELETE</c>).
    /// </summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>
    /// HTTP status code of the captured response (e.g. <c>200</c>, <c>404</c>, <c>500</c>).
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// URL path component of the captured request (e.g. <c>/api/orders/42</c>).
    /// </summary>
    public string UrlPath { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp at which the HTTP transaction was captured, with explicit UTC offset.
    /// <see cref="DateTimeOffset"/> is used instead of <see cref="DateTime"/> to match
    /// the domain type (<c>TransactionMetadata.TimestampUtc</c>) and guarantee the
    /// serialised wire value always carries an explicit <c>+00:00</c> offset suffix.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// Size of the gzip-compressed S3 object in bytes, as reported by S3.
    /// </summary>
    public long CompressedSizeBytes { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent modification to the S3 object,
    /// as reported by the S3 listing response, with explicit UTC offset.
    /// <see cref="DateTimeOffset"/> is used to match the domain type
    /// (<c>TransactionMetadata.S3LastModified</c>) and ensure unambiguous
    /// wire serialisation.
    /// </summary>
    public DateTimeOffset S3LastModified { get; init; }
}
