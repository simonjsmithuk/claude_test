namespace DataViewer.Application.DTOs.CredentialProfiles;

/// <summary>
/// Read model for a credential profile returned in API responses.
/// </summary>
/// <remarks>
/// ⚠️ Security (Product Spec § G-04): This DTO deliberately omits the
/// <c>EncryptedSecretKey</c> / <c>SecretAccessKey</c> field. The AWS Secret Access Key
/// is NEVER returned in any API response under any circumstances.
/// The mapping layer that projects from the <c>CredentialProfile</c> domain entity
/// to this DTO is the sole enforcement point for this rule.
/// </remarks>
public sealed record CredentialProfileDto
{
    /// <summary>Unique identifier of the credential profile.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Human-readable display name that uniquely identifies this profile.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// AWS IAM Access Key ID (the public portion of the credential pair).
    /// This is safe to expose — the Secret Access Key is never returned.
    /// </summary>
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>
    /// AWS region code where the target S3 bucket resides (e.g. <c>us-east-1</c>).
    /// </summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>
    /// Name of the S3 bucket that holds transaction record objects.
    /// </summary>
    public string BucketName { get; init; } = string.Empty;

    /// <summary>
    /// Optional S3 object key prefix used to scope listing and retrieval operations.
    /// <see langword="null"/> or empty means the entire bucket root is in scope.
    /// </summary>
    public string? KeyPrefix { get; init; }

    /// <summary>
    /// <see langword="true"/> when this profile is the currently selected default
    /// for search and retrieval operations.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// <see langword="true"/> when this profile has been soft-deleted by an Admin.
    /// Soft-deleted profiles are excluded from operational queries.
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// UTC timestamp when this profile was first created, with explicit UTC offset.
    /// </summary>
    /// <remarks>
    /// The underlying <c>CredentialProfile</c> domain entity uses <see cref="DateTime"/>
    /// for this field (infrastructure-managed). The mapping layer must convert via
    /// <c>DateTime.SpecifyKind(value, DateTimeKind.Utc)</c> before assigning to a
    /// <see cref="DateTimeOffset"/> to preserve the UTC guarantee on the wire.
    /// Using <see cref="DateTimeOffset"/> here ensures the serialised JSON always
    /// carries an explicit <c>+00:00</c> offset, eliminating client-side timezone
    /// ambiguity present when <see cref="DateTime"/> with <c>DateTimeKind.Unspecified</c>
    /// is serialised without a <c>Z</c> suffix.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent update to this profile, with explicit UTC offset.
    /// </summary>
    /// <remarks>
    /// See <see cref="CreatedAt"/> remarks for the mapping convention from the domain
    /// entity's <see cref="DateTime"/> field.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; init; }
}
