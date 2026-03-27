namespace DataViewer.Application.DTOs.Transactions;

/// <summary>
/// Full detail of a single HTTP transaction record, returned by
/// GET /api/transactions/{s3Key}.
/// Contains the parsed HTTP request and response with headers, body, and associated
/// S3 metadata (API Design § 5.4).
/// </summary>
/// <remarks>
/// Both <see cref="Request"/> and <see cref="Response"/> are typed as
/// <see cref="TransactionMessageDto"/> — a single shared type whose four fields
/// (<c>Headers</c>, <c>Body</c>, <c>BodyContentType</c>, <c>IsBodyTruncated</c>)
/// are structurally identical on both sides of an HTTP transaction. Consumers
/// distinguish request from response via property name, not type.
/// </remarks>
public sealed record TransactionDetailDto
{
    /// <summary>
    /// The parsed HTTP request portion of the transaction record.
    /// </summary>
    public TransactionMessageDto Request { get; init; } = new();

    /// <summary>
    /// The parsed HTTP response portion of the transaction record.
    /// </summary>
    public TransactionMessageDto Response { get; init; } = new();

    /// <summary>
    /// S3 storage metadata and capture-time attributes for this transaction record.
    /// </summary>
    public TransactionMetadataDto Metadata { get; init; } = new();
}
