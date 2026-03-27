using DataViewer.Domain.Entities;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Fluent test-data builder for <see cref="SystemSettings"/> instances.
/// </summary>
/// <remarks>
/// Provides sensible defaults so individual tests only need to override the
/// properties relevant to the scenario under test.
/// <para>
/// Because <see cref="SystemSettings.Id"/> has a private setter and is always
/// initialised to <c>1</c> by the public parameterless constructor, the builder
/// does not expose a setter for <c>Id</c> — it always produces the correct
/// singleton key automatically.
/// </para>
/// </remarks>
public sealed class SystemSettingsBuilder
{
    private int _jwtAccessTokenMinutes = 15;
    private int _jwtRefreshTokenHours = 24;
    private int _bodySizeCapMb = 10;
    private int _lockoutThreshold = 5;
    private DateTime _updatedAt = DateTime.UtcNow;

    public SystemSettingsBuilder WithJwtAccessTokenMinutes(int minutes)
    {
        _jwtAccessTokenMinutes = minutes;
        return this;
    }

    public SystemSettingsBuilder WithJwtRefreshTokenHours(int hours)
    {
        _jwtRefreshTokenHours = hours;
        return this;
    }

    public SystemSettingsBuilder WithBodySizeCapMb(int mb)
    {
        _bodySizeCapMb = mb;
        return this;
    }

    public SystemSettingsBuilder WithLockoutThreshold(int threshold)
    {
        _lockoutThreshold = threshold;
        return this;
    }

    public SystemSettingsBuilder WithUpdatedAt(DateTime updatedAt)
    {
        _updatedAt = updatedAt;
        return this;
    }

    /// <summary>
    /// Produces a <see cref="SystemSettings"/> instance (Id always = 1).
    /// </summary>
    public SystemSettings Build() => new()
    {
        JwtAccessTokenMinutes = _jwtAccessTokenMinutes,
        JwtRefreshTokenHours = _jwtRefreshTokenHours,
        BodySizeCapMb = _bodySizeCapMb,
        LockoutThreshold = _lockoutThreshold,
        UpdatedAt = _updatedAt
    };

    /// <summary>
    /// Returns a builder pre-configured with default production-equivalent values.
    /// </summary>
    public static SystemSettingsBuilder Defaults() => new();
}
