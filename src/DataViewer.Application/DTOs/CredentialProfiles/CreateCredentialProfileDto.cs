using System.ComponentModel.DataAnnotations;

namespace DataViewer.Application.DTOs.CredentialProfiles;

/// <summary>
/// Request payload for creating a new credential profile (Admin only).
/// Sent as the body of POST /api/credential-profiles.
/// </summary>
public sealed record CreateCredentialProfileDto
{
    /// <summary>
    /// Human-readable display name. Must be unique across all non-deleted profiles.
    /// </summary>
    [Required(ErrorMessage = "Profile name is required.")]
    [MaxLength(100, ErrorMessage = "Profile name must not exceed 100 characters.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// AWS IAM Access Key ID (the public, non-sensitive portion of the credential pair).
    /// Typically 20 uppercase alphanumeric characters (e.g. <c>AKIAIOSFODNN7EXAMPLE</c>).
    /// </summary>
    [Required(ErrorMessage = "Access Key ID is required.")]
    [MaxLength(128, ErrorMessage = "Access Key ID must not exceed 128 characters.")]
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>
    /// AWS Secret Access Key (the sensitive, private portion of the credential pair).
    /// This value is encrypted with AES-256-CBC before storage and is NEVER returned
    /// in any subsequent API response (Product Spec § G-04).
    /// </summary>
    [Required(ErrorMessage = "Secret Access Key is required.")]
    [MaxLength(512, ErrorMessage = "Secret Access Key must not exceed 512 characters.")]
    public string SecretAccessKey { get; init; } = string.Empty;

    /// <summary>
    /// AWS region code where the target S3 bucket resides (e.g. <c>us-east-1</c>).
    /// </summary>
    [Required(ErrorMessage = "Region is required.")]
    [MaxLength(50, ErrorMessage = "Region must not exceed 50 characters.")]
    public string Region { get; init; } = string.Empty;

    /// <summary>
    /// Name of the S3 bucket that holds transaction record objects.
    /// Must comply with S3 bucket naming rules (3–63 characters).
    /// </summary>
    [Required(ErrorMessage = "Bucket name is required.")]
    [MinLength(3, ErrorMessage = "Bucket name must be at least 3 characters.")]
    [MaxLength(63, ErrorMessage = "Bucket name must not exceed 63 characters.")]
    public string BucketName { get; init; } = string.Empty;

    /// <summary>
    /// Optional S3 object key prefix used to scope listing and retrieval operations.
    /// Include a trailing slash to target a virtual directory (e.g. <c>transactions/</c>).
    /// <see langword="null"/> or empty string means the entire bucket root is in scope.
    /// </summary>
    [MaxLength(1024, ErrorMessage = "Key prefix must not exceed 1024 characters.")]
    public string? KeyPrefix { get; init; }

    /// <summary>
    /// When <see langword="true"/>, this profile is immediately set as the active
    /// default; any existing active profile is deactivated within the same transaction.
    /// </summary>
    public bool SetAsActive { get; init; }
}
