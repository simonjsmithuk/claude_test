namespace DataViewer.Core.Domain;

/// <summary>Named AWS S3 credential profile (FR-06 through FR-11).</summary>
public class S3CredentialProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>AES-256 encrypted secret access key — never returned in API responses (FR-07, FR-08).</summary>
    public string EncryptedSecretAccessKey { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string? KeyPrefix { get; set; }
    public bool IsActive { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User CreatedByUser { get; set; } = null!;
}
