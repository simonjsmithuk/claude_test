namespace DataViewer.Application.DTOs.Transactions;

/// <summary>
/// S3 storage metadata for a transaction record, nested within
/// <see cref="TransactionDetailDto"/> (API Design § 5.4).
/// </summary>
/// <remarks>
/// All five fields directly correspond to the required metadata fields specified
/// in the acceptance criteria for TASK-006 and the API design § 5.4:
/// <c>s3Key</c>, <c>compressedSizeBytes</c>, <c>decompressedSizeBytes</c>,
/// <c>timestampUtc</c>, and <c>s3LastModified</c>.
/// </remarks>
public sealed record TransactionMetadataDto
{
    /// <summary>
    /// The full S3 object key that identifies this transaction record on S3.
    /// </summary>
    public string S3Key { get; init; } = string.Empty;

    /// <summary>
    /// Size of the gzip-compressed S3 object in bytes.
    /// </summary>
    public long CompressedSizeBytes { get; init; }

    /// <summary>
    /// Size of the S3 object body after gzip decompression, in bytes.
    /// </summary>
    public long DecompressedSizeBytes { get; init; }

    /// <summary>
    /// UTC timestamp at which the HTTP transaction was captured, with explicit UTC offset.
    /// <see cref="DateTimeOffset"/> is used instead of <see cref="DateTime"/> to match
    /// the domain type (<c>TransactionMetadata.TimestampUtc</c>) and guarantee the
    /// serialised wire value always carries an explicit <c>+00:00</c> offset suffix,
    /// eliminating client-side timezone ambiguity.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent modification to the S3 object,
    /// as reported by S3, with explicit UTC offset.
    /// <see cref="DateTimeOffset"/> is used to match the domain type
    /// (<c>TransactionMetadata.S3LastModified</c>) and ensure unambiguous
    /// wire serialisation.
    /// </summary>
    public DateTimeOffset S3LastModified { get; init; }
}
