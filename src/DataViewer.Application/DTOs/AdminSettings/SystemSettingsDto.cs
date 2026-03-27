namespace DataViewer.Application.DTOs.AdminSettings;

/// <summary>
/// ⚠️ Obsolete: This dual-use DTO has been replaced by separate
/// <see cref="SystemSettingsRequestDto"/> (for PUT /api/admin/settings) and
/// <see cref="SystemSettingsResponseDto"/> (for GET /api/admin/settings).
/// </summary>
/// <remarks>
/// The original single DTO was shared between the GET response and the PUT request body.
/// This caused a correctness gap: the <c>UpdatedAt</c> field is server-managed and
/// must never be accepted in a PUT request payload. Model binding would silently
/// accept a client-supplied value, which could either be inadvertently used (data
/// integrity bug) or silently ignored (ambiguous API contract).
/// <para>
/// Callers should migrate to:
/// <list type="bullet">
///   <item><see cref="SystemSettingsRequestDto"/> for PUT request bodies.</item>
///   <item><see cref="SystemSettingsResponseDto"/> for GET response bodies.</item>
/// </list>
/// </para>
/// </remarks>
[Obsolete(
    "Use SystemSettingsRequestDto for PUT /api/admin/settings request bodies " +
    "and SystemSettingsResponseDto for GET /api/admin/settings response bodies. " +
    "This type will be removed in a future release.",
    error: false)]
public sealed record SystemSettingsDto
{
    /// <inheritdoc cref="SystemSettingsRequestDto.JwtAccessTokenMinutes"/>
    public int JwtAccessTokenMinutes { get; init; } = 15;

    /// <inheritdoc cref="SystemSettingsRequestDto.JwtRefreshTokenHours"/>
    public int JwtRefreshTokenHours { get; init; } = 24;

    /// <inheritdoc cref="SystemSettingsRequestDto.BodySizeCapMb"/>
    public int BodySizeCapMb { get; init; } = 10;

    /// <inheritdoc cref="SystemSettingsRequestDto.LockoutThreshold"/>
    public int LockoutThreshold { get; init; } = 5;

    /// <inheritdoc cref="SystemSettingsResponseDto.UpdatedAt"/>
    public DateTimeOffset UpdatedAt { get; init; }
}
