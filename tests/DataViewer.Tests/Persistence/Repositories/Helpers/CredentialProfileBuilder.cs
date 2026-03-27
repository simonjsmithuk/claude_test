using DataViewer.Domain.Entities;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Fluent test-data builder for <see cref="CredentialProfile"/> instances.
/// </summary>
/// <remarks>
/// Provides sensible defaults so individual tests only need to override the
/// properties relevant to the scenario under test, keeping tests concise.
/// </remarks>
public sealed class CredentialProfileBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Test Profile";
    private string _accessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private byte[] _encryptedSecretKey = [0x01, 0x02, 0x03];
    private string _region = "us-east-1";
    private string _bucketName = "test-bucket";
    private string? _keyPrefix = null;
    private bool _isActive = false;
    private bool _isDeleted = false;
    private DateTime _createdAt = DateTime.UtcNow.AddDays(-1);
    private DateTime _updatedAt = DateTime.UtcNow.AddDays(-1);
    private Guid _createdByUserId = Guid.NewGuid();

    public CredentialProfileBuilder WithId(Guid id) { _id = id; return this; }
    public CredentialProfileBuilder WithName(string name) { _name = name; return this; }
    public CredentialProfileBuilder WithAccessKeyId(string accessKeyId) { _accessKeyId = accessKeyId; return this; }
    public CredentialProfileBuilder WithEncryptedSecretKey(byte[] key) { _encryptedSecretKey = key; return this; }
    public CredentialProfileBuilder WithRegion(string region) { _region = region; return this; }
    public CredentialProfileBuilder WithBucketName(string bucketName) { _bucketName = bucketName; return this; }
    public CredentialProfileBuilder WithKeyPrefix(string? keyPrefix) { _keyPrefix = keyPrefix; return this; }
    public CredentialProfileBuilder WithIsActive(bool isActive) { _isActive = isActive; return this; }
    public CredentialProfileBuilder WithIsDeleted(bool isDeleted) { _isDeleted = isDeleted; return this; }
    public CredentialProfileBuilder WithCreatedAt(DateTime createdAt) { _createdAt = createdAt; return this; }
    public CredentialProfileBuilder WithUpdatedAt(DateTime updatedAt) { _updatedAt = updatedAt; return this; }
    public CredentialProfileBuilder WithCreatedByUserId(Guid userId) { _createdByUserId = userId; return this; }

    /// <summary>Produces a <see cref="CredentialProfile"/> with the configured properties.</summary>
    public CredentialProfile Build() => new()
    {
        Id = _id,
        Name = _name,
        AccessKeyId = _accessKeyId,
        EncryptedSecretKey = _encryptedSecretKey,
        Region = _region,
        BucketName = _bucketName,
        KeyPrefix = _keyPrefix,
        IsActive = _isActive,
        IsDeleted = _isDeleted,
        CreatedAt = _createdAt,
        UpdatedAt = _updatedAt,
        CreatedByUserId = _createdByUserId,
    };
}
