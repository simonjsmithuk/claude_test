namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;

/// <summary>
/// Provides read and write access to the singleton <see cref="SystemSettings"/> row.
/// </summary>
/// <remarks>
/// The <see cref="SystemSettings"/> table always contains exactly one row with
/// <see cref="SystemSettings.Id"/> = 1. The application layer never inserts a second
/// row — all mutations are upserts against the fixed primary key.
///
/// <para>
/// Implementations must seed the singleton row (with default values) in the EF Core
/// migration so that <see cref="GetAsync"/> never returns <see langword="null"/> on a
/// correctly initialised database.
/// </para>
/// </remarks>
public interface ISystemSettingsRepository
{
    /// <summary>
    /// Retrieves the singleton <see cref="SystemSettings"/> record.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The single <see cref="SystemSettings"/> instance. Returns <see langword="null"/>
    /// only when the database has not been seeded (should not occur in production;
    /// callers should treat a <see langword="null"/> result as a fatal configuration error).
    /// </returns>
    Task<SystemSettings?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists updated values to the singleton <see cref="SystemSettings"/> record.
    /// </summary>
    /// <remarks>
    /// Implementations must perform an upsert against the well-known primary key
    /// (<c>Id = 1</c>) rather than an unconditional INSERT, to guard against accidental
    /// duplicate rows if the seed migration is re-run.
    /// The Infrastructure layer must refresh <see cref="SystemSettings.UpdatedAt"/>
    /// to the current UTC time on every successful persist.
    /// </remarks>
    /// <param name="settings">
    /// The <see cref="SystemSettings"/> instance containing the new values.
    /// <see cref="SystemSettings.Id"/> must be 1.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the update has been committed.</returns>
    Task UpdateAsync(SystemSettings settings, CancellationToken cancellationToken);
}
