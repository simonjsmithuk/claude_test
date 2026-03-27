#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Represents a named AWS S3 credential profile used to read transaction records.
/// Secret Access Keys are stored AES-256 encrypted and are never returned by any API endpoint.
/// </summary>
/// <remarks>
/// Deletion is soft: <see cref="IsDeleted"/> is set to <see langword="true"/> rather than
/// removing the row, preserving the foreign-key integrity of any audit log entries that
/// reference the profile name.
/// Only one profile may have <see cref="IsActive"/> set to <see langword="true"/> at a time;
/// this invariant is enforced at the application layer.
/// </remarks>
public class CredentialProfile
{
    /// <summary>Primary key — generated on creation, never reassigned.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable display name that uniquely identifies this profile.
    /// Must be non-empty; uniqueness is enforced at the application layer.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// AWS IAM Access Key ID (public portion of the credential pair).
    /// Stored in plain text; not considered sensitive on its own.
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>
    /// AES-256 encrypted AWS Secret Access Key.
    /// Stored as a raw byte array containing the IV prepended to the ciphertext.
    /// Never returned in any API response.
    /// </summary>
    public byte[] EncryptedSecretKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// AWS region code where the target bucket is located (e.g. <c>us-east-1</c>).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Name of the S3 bucket that holds transaction record objects.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional S3 object key prefix used to scope listing and retrieval operations.
    /// <see langword="null"/> or empty string means the entire bucket root is accessible.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// <see langword="true"/> when this profile is currently the selected default
    /// used for search and retrieval operations.
    /// At most one profile is active at any given time.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// <see langword="true"/> when this profile has been soft-deleted by an Admin.
    /// Soft-deleted profiles are excluded from all operational queries.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp when this profile was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent update to any field on this profile.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The <see cref="User.Id"/> of the Admin who created this profile.
    /// Stored as a plain foreign key value; no navigation property to avoid circular dependencies
    /// in serialisation scenarios. Resolved via the repository when needed.
    /// </summary>
    public Guid CreatedByUserId { get; set; }
}
