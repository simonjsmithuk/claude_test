namespace DataViewer.Application.DTOs.CredentialProfiles;

/// <summary>
/// Response payload returned after a POST /api/credential-profiles/{id}/test-connection call.
/// Conveys whether the S3 credentials and bucket configuration are reachable.
/// </summary>
public sealed record TestConnectionResultDto
{
    /// <summary>
    /// <see langword="true"/> when the connectivity test succeeded — the supplied AWS
    /// credentials are valid and the configured S3 bucket is accessible.
    /// <see langword="false"/> when the test failed for any reason (invalid credentials,
    /// wrong region, bucket not found, network error, etc.).
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// A human-readable message describing the outcome of the connection test.
    /// On success this is typically a brief confirmation (e.g. "Connection successful.").
    /// On failure this contains a non-sensitive description of the error suitable for
    /// display to an Admin (e.g. "Invalid credentials." or "Bucket not found.").
    /// AWS error details that may contain sensitive region or account information
    /// must be sanitised before populating this field.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp at which the connectivity test was performed, with explicit UTC offset.
    /// <see cref="DateTimeOffset"/> is used instead of <see cref="DateTime"/> so the
    /// serialised wire value always carries an explicit <c>+00:00</c> offset suffix,
    /// making it unambiguous to all JSON consumers regardless of their local timezone.
    /// Useful for the Admin to know whether the result is fresh or cached.
    /// </summary>
    public DateTimeOffset TestedAt { get; init; }
}
