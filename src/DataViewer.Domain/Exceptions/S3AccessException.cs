namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an operation against AWS S3 fails due to an access error such as
/// invalid credentials, insufficient IAM permissions, a missing bucket or object,
/// a network timeout, or service-side throttling.
/// </summary>
/// <remarks>
/// Typical API-layer mappings:
/// <list type="bullet">
///   <item>
///     <description>
///       <c>HTTP 502 Bad Gateway</c> — the upstream S3 service returned an error
///       or was unreachable (e.g. <c>ServiceUnavailable</c>, <c>RequestTimeout</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>HTTP 403 Forbidden</c> — when <see cref="ErrorCode"/> indicates a
///       permission denial (e.g. <c>AccessDenied</c>, <c>InvalidAccessKeyId</c>).
///     </description>
///   </item>
/// </list>
/// <para>
/// <see cref="S3Key"/> identifies the specific S3 object being accessed when the
/// error occurred. It is <see langword="null"/> for bucket-level or profile-level
/// operations (e.g. listing a bucket prefix) where a single object key cannot be
/// attributed.
/// </para>
/// </remarks>
public sealed class S3AccessException : DomainException
{
    /// <summary>
    /// Initialises the exception for an S3 operation failure.
    /// </summary>
    /// <param name="message">Human-readable description of the S3 access error.</param>
    /// <param name="errorCode">
    /// The AWS SDK or S3 service error code that identifies the failure category
    /// (e.g. <c>"AccessDenied"</c>, <c>"NoSuchBucket"</c>, <c>"NoSuchKey"</c>,
    /// <c>"RequestTimeout"</c>, <c>"ServiceUnavailable"</c>,
    /// <c>"InvalidAccessKeyId"</c>).
    /// </param>
    /// <param name="s3Key">
    /// The S3 object key being accessed when the error occurred, or
    /// <see langword="null"/> for bucket-level operations.
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
    /// The AWS SDK or S3 service error code that identifies the failure category.
    /// </param>
    /// <param name="s3Key">
    /// The S3 object key being accessed when the error occurred, or
    /// <see langword="null"/> for bucket-level operations.
    /// </param>
    /// <param name="innerException">
    /// The lower-level AWS SDK exception (e.g. <c>AmazonS3Exception</c>) that
    /// caused this fault. Preserved for structured logging; must not be forwarded
    /// verbatim to API consumers.
    /// </param>
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
    /// The AWS SDK or S3 service error code that identifies the failure category.
    /// <para>
    /// Common values:
    /// <list type="bullet">
    ///   <item><description><c>AccessDenied</c> — IAM permissions are insufficient for the requested operation.</description></item>
    ///   <item><description><c>InvalidAccessKeyId</c> — The supplied AWS Access Key ID does not exist.</description></item>
    ///   <item><description><c>NoSuchBucket</c> — The specified bucket does not exist in the configured region.</description></item>
    ///   <item><description><c>NoSuchKey</c> — The requested object key is not present in the bucket.</description></item>
    ///   <item><description><c>RequestTimeout</c> — The S3 request exceeded the configured timeout.</description></item>
    ///   <item><description><c>ServiceUnavailable</c> — AWS S3 returned a transient 503 response.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// The S3 object key that was being accessed when the error occurred.
    /// <see langword="null"/> for bucket-level or credential-profile-level
    /// operations where no single key is attributable.
    /// </summary>
    public string? S3Key { get; }
}
