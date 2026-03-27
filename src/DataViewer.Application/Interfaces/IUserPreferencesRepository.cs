namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;

/// <summary>
/// Provides persistence and retrieval of per-user UI preferences.
/// </summary>
/// <remarks>
/// <see cref="UserPreference"/> uses a shared-primary-key pattern where
/// <see cref="UserPreference.UserId"/> is simultaneously the primary key and the
/// foreign key to <see cref="User"/>. This enforces the one-to-one cardinality at
/// the database level without an additional unique index.
///
/// <para>
/// A preference row may not exist for a given user until they explicitly save their
/// preferences. The application layer falls back to default constants (e.g.
/// <see cref="UserPreference.DefaultPageSize"/>) when <see cref="GetByUserIdAsync"/>
/// returns <see langword="null"/>.
/// </para>
/// </remarks>
public interface IUserPreferencesRepository
{
    /// <summary>
    /// Retrieves the persisted preferences for the specified user.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="User.Id"/> of the user whose preferences to retrieve.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The <see cref="UserPreference"/> for the specified user, or
    /// <see langword="null"/> when the user has never explicitly saved preferences.
    /// Callers must handle the <see langword="null"/> case by applying application-layer
    /// defaults.
    /// </returns>
    Task<UserPreference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates the preferences for the specified user in a single
    /// atomic operation.
    /// </summary>
    /// <remarks>
    /// Upsert semantics: if a <see cref="UserPreference"/> row already exists for
    /// <see cref="UserPreference.UserId"/>, all fields are updated; if no row exists,
    /// a new row is inserted. This prevents race conditions in concurrent
    /// first-time-save scenarios.
    /// </remarks>
    /// <param name="preferences">
    /// The <see cref="UserPreference"/> instance to persist.
    /// <see cref="UserPreference.UserId"/> must match an existing <see cref="User.Id"/>.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the upsert has been committed.</returns>
    Task UpsertAsync(UserPreference preferences, CancellationToken cancellationToken);
}
