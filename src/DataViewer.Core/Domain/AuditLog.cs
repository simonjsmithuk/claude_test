namespace DataViewer.Core.Domain;

/// <summary>
/// Immutable audit log entry. Never updated or deleted via application APIs (FR-26).
/// Covers search operations (FR-23), record views (FR-24), and credential changes (FR-25).
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Parameters { get; set; }  // JSON-serialised filter params or S3 key
    public string? IpAddress { get; set; }
    public int? ResultCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}

/// <summary>Well-known audit action constants.</summary>
public static class AuditActions
{
    public const string Search           = "SEARCH";
    public const string RecordView       = "RECORD_VIEW";
    public const string CredentialCreate = "CREDENTIAL_CREATE";
    public const string CredentialUpdate = "CREDENTIAL_UPDATE";
    public const string CredentialDelete = "CREDENTIAL_DELETE";
    public const string CredentialTest   = "CREDENTIAL_TEST";
    public const string Login            = "LOGIN";
    public const string Logout           = "LOGOUT";
    public const string LoginFailed      = "LOGIN_FAILED";
}
