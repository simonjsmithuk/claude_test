namespace DataViewer.Core.Domain;

/// <summary>
/// Dynamic application settings stored in the database (FR-29).
/// Key/value pairs editable by Admins only.
/// </summary>
public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Well-known setting keys (FR-29).</summary>
public static class SettingKeys
{
    public const string JwtAccessTokenLifetimeMinutes  = "Jwt:AccessTokenLifetimeMinutes";
    public const string JwtRefreshTokenLifetimeHours   = "Jwt:RefreshTokenLifetimeHours";
    public const string BodyMaxSizeBytes               = "S3:BodyMaxSizeBytes";
    public const string AccountLockoutThreshold        = "Auth:LockoutThreshold";
}
