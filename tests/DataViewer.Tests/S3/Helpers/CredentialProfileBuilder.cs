using DataViewer.Domain.Entities;

namespace DataViewer.Tests.S3.Helpers;

/// <summary>
/// Fluent test-data builder for <see cref="CredentialProfile"/> instances used in
/// S3 infrastructure tests.
/// </summary>
internal sealed class CredentialProfileBuilder
{
    private Guid   _id              = Guid.NewGuid();
    private string _name            = "test-profile";
    private string _accessKeyId     = "AKIAIOSFODNN7EXAMPLE";
    private string _region          = "eu-west-2";
    private string _bucketName      = "test-bucket";
    private string? _keyPrefix      = null;
    private bool   _isActive        = true;
    private bool   _isDeleted       = false;
    private Guid   _createdByUserId = Guid.NewGuid();

    // ── Fluent setters ────────────────────────────────────────────────────────

    internal CredentialProfileBuilder WithId(Guid id)           { _id = id;              return this; }
    internal CredentialProfileBuilder WithName(string name)     { _name = name;          return this; }
    internal CredentialProfileBuilder WithAccessKeyId(string k) { _accessKeyId = k;      return this; }
    internal CredentialProfileBuilder WithRegion(string r)      { _region = r;           return this; }
    internal CredentialProfileBuilder WithBucketName(string b)  { _bucketName = b;       return this; }
    internal CredentialProfileBuilder WithKeyPrefix(string? p)  { _keyPrefix = p;        return this; }
    internal CredentialProfileBuilder AsActive()                 { _isActive = true;      return this; }
    internal CredentialProfileBuilder AsInactive()               { _isActive = false;     return this; }
    internal CredentialProfileBuilder AsDeleted()                { _isDeleted = true;     return this; }
    internal CredentialProfileBuilder WithNullAccessKeyId()      { _accessKeyId = null!;  return this; }
    internal CredentialProfileBuilder WithEmptyAccessKeyId()     { _accessKeyId = "";     return this; }
    internal CredentialProfileBuilder WithNullRegion()           { _region = null!;       return this; }
    internal CredentialProfileBuilder WithEmptyRegion()          { _region = "";          return this; }

    // ── Build ─────────────────────────────────────────────────────────────────

    internal CredentialProfile Build() => new()
    {
        Id              = _id,
        Name            = _name,
        AccessKeyId     = _accessKeyId,
        Region          = _region,
        BucketName      = _bucketName,
        KeyPrefix       = _keyPrefix,
        IsActive        = _isActive,
        IsDeleted       = _isDeleted,
        CreatedByUserId = _createdByUserId,
        CreatedAt       = DateTime.UtcNow,
        UpdatedAt       = DateTime.UtcNow,
        // EncryptedSecretKey left as the default empty array — tests supply the
        // decrypted key string directly to the factory / service under test.
        EncryptedSecretKey = [],
    };

    // ── Convenience factory methods ───────────────────────────────────────────

    internal static CredentialProfileBuilder AValid() => new();

    internal static CredentialProfileBuilder AValidWithPrefix(string prefix) =>
        new CredentialProfileBuilder().WithKeyPrefix(prefix);
}
