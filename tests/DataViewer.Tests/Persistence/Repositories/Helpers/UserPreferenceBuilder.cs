using DataViewer.Domain.Entities;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Fluent test-data builder for <see cref="UserPreference"/> instances.
/// </summary>
/// <remarks>
/// Provides sensible defaults so individual tests only need to override the
/// properties relevant to the scenario under test.
/// </remarks>
public sealed class UserPreferenceBuilder
{
    private Guid _userId = Guid.NewGuid();
    private int _defaultPageSize = 25;
    private int _defaultDateRangeDays = 7;
    private Guid? _preferredProfileId = null;

    public UserPreferenceBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public UserPreferenceBuilder WithDefaultPageSize(int pageSize)
    {
        _defaultPageSize = pageSize;
        return this;
    }

    public UserPreferenceBuilder WithDefaultDateRangeDays(int days)
    {
        _defaultDateRangeDays = days;
        return this;
    }

    public UserPreferenceBuilder WithPreferredProfileId(Guid? profileId)
    {
        _preferredProfileId = profileId;
        return this;
    }

    /// <summary>
    /// Produces a <see cref="UserPreference"/> with the configured properties.
    /// </summary>
    public UserPreference Build() => new()
    {
        UserId = _userId,
        DefaultPageSize = _defaultPageSize,
        DefaultDateRangeDays = _defaultDateRangeDays,
        PreferredProfileId = _preferredProfileId
    };

    /// <summary>
    /// Returns a builder pre-configured with default values for the given user.
    /// </summary>
    public static UserPreferenceBuilder ForUser(Guid userId) =>
        new UserPreferenceBuilder().WithUserId(userId);
}
