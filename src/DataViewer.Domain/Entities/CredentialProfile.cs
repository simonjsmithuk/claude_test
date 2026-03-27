namespace DataViewer.Domain.Entities;

/// <summary>
/// Represents a named AWS S3 credential profile used to browse and retrieve
/// HTTP transaction records stored in an S3 bucket.
/// </summary>
/// <remarks>
/// The AWS Secret Access Key is stored AES-256-CBC encrypted as a raw byte array
/// (<see cref="EncryptedSecretKey"/>). The byte array layout is
/// <c>[<see cref="IvSizeBytes"/>-byte IV] + [ciphertext]</c>; the encryption/decryption
/// logic lives exclusively in the Infrastructure layer and is never exposed to callers
/// of API endpoints.
///
/// <para>
/// <see cref="EncryptedSecretKey"/> MUST NOT be included in any API response. Enforce
/// this by projecting to a DTO at the controller/service boundary — never return this
/// entity directly from a controller action. The Domain layer carries no serialisation
/// attributes; the DTO mapping layer is the sole enforcement point.
/// </para>
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
    /// Size in bytes of the AES-256-CBC initialisation vector prepended to
    /// <see cref="EncryptedSecretKey"/>. AES always uses a 128-bit (16-byte) IV
    /// regardless of key length.
    /// </summary>
    /// <remarks>
    /// Reference this constant in the Infrastructure AES decrypt logic rather than
    /// hardcoding <c>16</c>, so that the domain contract and the encryption
    /// implementation remain in sync.
    /// </remarks>
    public const int IvSizeBytes = 16;

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
    /// Stored as a raw byte array with layout
    /// <c>[<see cref="IvSizeBytes"/>-byte IV] + [ciphertext]</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Security: This value MUST NEVER be included in any API response.
    /// Enforce DTO projection at the controller/service boundary — never return
    /// <see cref="CredentialProfile"/> directly from a controller action.
    /// The Infrastructure layer decrypts this field in-memory only when constructing
    /// an <c>AmazonS3Client</c>; it is not mapped to any response DTO.
    /// No serialisation attribute is placed here — the API/Infrastructure layer
    /// is the sole and correct enforcement point.
    /// </remarks>
    public byte[] EncryptedSecretKey { get; set; } = [];

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

    /// <summary>
    /// UTC timestamp when this profile was first created.
    /// Initialised to <see langword="default"/> here; the Infrastructure layer
    /// (EF Core <c>SaveChanges</c> interceptor or database <c>DEFAULT CURRENT_TIMESTAMP</c>)
    /// is the authoritative writer so the persisted value reflects the actual
    /// database write time rather than the in-memory object construction time.
    /// </summary>
    /// <remarks>
    /// ⚠️ Risk: if the Infrastructure interceptor is missed, this field persists as
    /// <c>DateTimeOffset.MinValue</c> (0001-01-01). Monitor this via integration tests.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; set; } = default;

    /// <summary>
    /// UTC timestamp of the most recent update to any field on this profile.
    /// Initialised to <see langword="default"/> here; the Infrastructure layer
    /// must refresh this value on every <c>UPDATE</c> operation (e.g. via a
    /// <c>SaveChanges</c> interceptor) to avoid stale construction-time timestamps.
    /// </summary>
    /// <remarks>
    /// ⚠️ Risk: if the Infrastructure interceptor is missed, this field persists as
    /// <c>DateTimeOffset.MinValue</c> (0001-01-01). Monitor this via integration tests.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; set; } = default;

    /// <summary>
    /// The <see cref="User.Id"/> of the Admin who created this profile.
    /// Stored as a plain foreign-key value with no navigation property because
    /// the creating user's full entity is not needed by any current application
    /// query against credential profiles.
    /// By design — no navigation property. See ADR-004.
    /// </summary>
    public Guid CreatedByUserId { get; set; }
}
