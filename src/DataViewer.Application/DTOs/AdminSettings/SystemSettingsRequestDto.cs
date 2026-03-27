using System.ComponentModel.DataAnnotations;

namespace DataViewer.Application.DTOs.AdminSettings;

/// <summary>
/// Request payload for the PUT /api/admin/settings endpoint.
/// Contains only the fields that an Admin may supply when updating system settings.
/// </summary>
/// <remarks>
/// Splitting the system settings into separate request and response DTOs
/// (<see cref="SystemSettingsRequestDto"/> / <see cref="SystemSettingsResponseDto"/>)
/// enforces the "read-only in requests" semantic for server-managed fields such as
/// <c>UpdatedAt</c>. A single dual-use DTO would allow model binding to accept an
/// Admin-supplied <c>UpdatedAt</c> value in the PUT body, which would either be
/// silently ignored (waste) or, if inadvertently used, constitute a data integrity bug.
/// </remarks>
public sealed record SystemSettingsRequestDto
{
    /// <summary>
    /// Lifetime of a JWT access token in whole minutes.
    /// Recommended range: 5–60 minutes.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "JwtAccessTokenMinutes must be between 1 and 1440 (24 hours).")]
    public int JwtAccessTokenMinutes { get; init; } = 15;

    /// <summary>
    /// Lifetime of a refresh token in whole hours.
    /// Recommended range: 1–720 hours (up to 30 days).
    /// </summary>
    [Range(1, 720, ErrorMessage = "JwtRefreshTokenHours must be between 1 and 720.")]
    public int JwtRefreshTokenHours { get; init; } = 24;

    /// <summary>
    /// Maximum size in megabytes of an S3 object body that the API will decompress
    /// and return. Requests exceeding this cap are rejected with HTTP 413.
    /// </summary>
    [Range(1, 500, ErrorMessage = "BodySizeCapMb must be between 1 and 500 MB.")]
    public int BodySizeCapMb { get; init; } = 10;

    /// <summary>
    /// Number of consecutive failed login attempts before an account is automatically
    /// locked. A value of <c>0</c> disables automatic lockout entirely.
    /// </summary>
    [Range(0, 100, ErrorMessage = "LockoutThreshold must be between 0 and 100.")]
    public int LockoutThreshold { get; init; } = 5;
}
