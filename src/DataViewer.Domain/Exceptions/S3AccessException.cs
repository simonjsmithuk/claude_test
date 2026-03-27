namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an operation against AWS S3 fails due to an access error —
/// for example: invalid credentials, insufficient IAM permissions, bucket not found,
/// network timeout, or service-side throttling.
/// </summary>
/// <remarks>
/// Maps to HTTP 502 Bad Gateway in the API layer (the upstream S3 service is
/// unavailable or returned an error), or HTTP 403 Forbidden when
/// <see cref="ErrorCode"/> indicates a permission denial.
/// <para>
/// <see cref="S3Key"/> identifies the specific object that was being accessed when
/// the error occurred. It is <see langword="null"/> for errors raised during
/// bucket-level operations (e.g. listing) where a single key is not applicable.
/// </para>
/// </remarks>
public sealed class S3AccessException : DomainException
{
    /// <summary>
    /// Initialises the exception for an S3 operation failure.
    /// </summary>
    /// <param name="message">Human-readable description of the S3 access error.</param>
    /// <param name="errorCode">
    /// The S3 or AWS SDK error code identifying the failure category
    /// (e.g. <c>"AccessDenied"</c>, <c>"NoSuchBucket"</c>, <c>"NoSuchKey"</c>,
    /// <c>"RequestTimeout"</c>, <c>"ServiceUnavailable"</c>).
    /// </param>
    /// <param name="s3Key">
    /// The S3 object key that was being accessed when the error occurred,
    /// or <see langword="null"/> for bucket-level operations.
    /// </param>
    public S3AccessException(string message, string errorCode, string? s3Key = null)
        : base(message)
    {
        ErrorCode = errorCode;
        S3Key = s3Key;
    }

    /// <summary>
    /// Initialises the exception for an S3 operation failure, preserving the
    /// lower-level AWS SDK exception as the inner cause.
    /// </summary>
    /// <param name="message">Human-readable description of the S3 access error.</param>
    /// <param name="errorCode">
    /// The S3 or AWS SDK error code identifying the failure category.
    /// </param>
    /// <param name="s3Key">
    /// The S3 object key that was being accessed when the error occurred,
    /// or <see langword="null"/> for bucket-level operations.
    /// </param>
    /// <param name="innerException">The lower-level AWS SDK exception that caused this fault.</param>
    public S3AccessException(
        string message,
        string errorCode,
        string? s3Key,
        Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        S3Key = s3Key;
    }

    // ── S3-specific properties ───────────────────────────────────────────────

    /// <summary>
    /// The S3 / AWS SDK error code that identifies the failure category.
    /// Common values include:
    /// <list type="bullet">
    ///   <item><description><c>AccessDenied</c> — IAM permissions insufficient.</description></item>
    ///   <item><description><c>InvalidAccessKeyId</c> — The supplied Access Key ID does not exist.</description></item>
    ///   <item><description><c>NoSuchBucket</c> — The specified bucket does not exist in the given region.</description></item>
    ///   <item><description><c>NoSuchKey</c> — The requested object key is not present in the bucket.</description></item>
    ///   <item><description><c>RequestTimeout</c> — The S3 request timed out.</description></item>
    ///   <item><description><c>ServiceUnavailable</c> — AWS S3 returned a 503 response.</description></item>
    /// </list>
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// The S3 object key that was being accessed when the error occurred.
    /// <see langword="null"/> for bucket-level or profile-level operations
    /// where no single key can be identified.
    /// </summary>
    public string? S3Key { get; }
}
