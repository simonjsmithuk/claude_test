#nullable enable

namespace DataViewer.Domain.Entities;

/// <summary>
/// Represents a named AWS S3 credential profile used to browse and retrieve
/// HTTP transaction records stored in an S3 bucket.
/// </summary>
/// <remarks>
/// The AWS Secret Access Key is stored AES-256 encrypted as a raw byte array
/// (<see cref="EncryptedSecretKey"/>). The byte array layout is
/// <c>[16-byte IV] + [ciphertext]</c>; the encryption/decryption logic lives
/// exclusively in the Infrastructure layer and is never exposed to callers of
/// API endpoints.
///
/// <para>
/// Deletion is soft: <see cref="IsDeleted"/> is set to <see langword="true"/>
/// rather than removing the row. This preserves foreign-key integrity with any
/// audit log entries that snapshot the profile name at action time.
/// </para>
///
/// <para>
/// At most one profile may have <see cref="IsActive"/> set to
/// <see langword="true"/> at a time. This invariant is enforced at the
/// application layer within a database transaction, not at the entity level.
/// </para>
/// </remarks>
public class CredentialProfile
{
    /// <summary>
    /// Primary key. Generated once on entity construction; never reassigned.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable display name that uniquely identifies this profile.
    /// Must be non-empty; uniqueness is enforced by a database index and validated
    /// at the application layer before persistence.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// AWS IAM Access Key ID (the public portion of the credential pair).
    /// Stored in plain text — the Access Key ID alone is not considered sensitive.
    /// Format: 20 uppercase alphanumeric characters (e.g. <c>AKIAIOSFODNN7EXAMPLE</c>).
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-CBC encrypted AWS Secret Access Key.
    /// Stored as a raw byte array with layout <c>[16-byte IV] + [ciphertext]</c>.
    /// This value is never included in any API response payload; the Infrastructure
    /// layer decrypts it in-memory only when constructing an <c>AmazonS3Client</c>.
    /// </summary>
    public byte[] EncryptedSecretKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// AWS region code identifying where the target bucket is located.
    /// Examples: <c>us-east-1</c>, <c>eu-west-2</c>, <c>ap-southeast-1</c>.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Name of the S3 bucket that holds transaction record objects.
    /// Must comply with S3 bucket naming rules (3–63 lowercase alphanumeric characters
    /// and hyphens; no underscores; not IP-address format).
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional S3 object key prefix used to scope <c>ListObjectsV2</c> and
    /// <c>GetObject</c> operations to a sub-path within the bucket.
    /// <see langword="null"/> or empty string means the entire bucket root is in scope.
    /// If supplied, the prefix is applied verbatim — the caller must include a
    /// trailing slash when targeting a virtual directory.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// <see langword="true"/> when this profile is the currently selected default
    /// used for search and retrieval operations.
    /// At most one profile is active at any given time across the entire system.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// <see langword="true"/> when this profile has been soft-deleted by an Admin.
    /// Soft-deleted profiles are excluded from all operational queries (search,
    /// retrieval, profile picker) but are retained for audit log integrity.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp when this profile was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent update to any field on this profile.
    /// Must be refreshed by the application layer on every <c>UPDATE</c> operation.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The <see cref="User.Id"/> of the Admin who created this profile.
    /// Stored as a plain value (no navigation property) to avoid circular
    /// serialisation issues and because the creating user's full entity is
    /// rarely needed when loading profile data.
    /// </summary>
    public Guid CreatedByUserId { get; set; }
}
