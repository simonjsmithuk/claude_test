namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;

/// <summary>
/// Provides CRUD and lifecycle management for <see cref="CredentialProfile"/> entities.
/// </summary>
/// <remarks>
/// Credential profiles are soft-deleted, never hard-deleted, to preserve foreign-key
/// integrity with audit log entries that snapshot the profile name at action time
/// (see <see cref="CredentialProfile.IsDeleted"/> and <see cref="SoftDeleteAsync"/>).
///
/// <para>
/// At most one profile may have <see cref="CredentialProfile.IsActive"/> set to
/// <see langword="true"/> at any time. Implementations of <see cref="ActivateAsync"/>
/// must enforce this invariant atomically within a database transaction (set the target
/// profile active, clear <c>IsActive</c> on all other profiles in the same transaction).
/// </para>
///
/// <para>
/// All query methods that accept an <c>includeDeleted</c> parameter default to
/// excluding soft-deleted profiles, ensuring that deleted profiles are invisible to
/// normal application flows while remaining accessible for audit-log reconciliation.
/// </para>
/// </remarks>
public interface ICredentialProfileRepository
{
    /// <summary>
    /// Retrieves a single credential profile by its primary key.
    /// </summary>
    /// <param name="id">The <see cref="CredentialProfile.Id"/> to look up.</param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The matching <see cref="CredentialProfile"/>, or <see langword="null"/> when no
    /// profile with the specified <paramref name="id"/> exists (including when the
    /// profile has been soft-deleted).
    /// </returns>
    Task<CredentialProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all credential profiles, with an option to include soft-deleted ones.
    /// </summary>
    /// <param name="includeDeleted">
    /// When <see langword="true"/>, soft-deleted profiles are included in the result.
    /// Defaults to <see langword="false"/> so that deleted profiles are hidden from
    /// normal application flows.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A read-only list of all <see cref="CredentialProfile"/> records matching the
    /// filter, ordered by <see cref="CredentialProfile.CreatedAt"/> ascending.
    /// Returns an empty list when no profiles exist.
    /// </returns>
    Task<IReadOnlyList<CredentialProfile>> GetAllAsync(
        bool includeDeleted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the single profile currently marked as active
    /// (<see cref="CredentialProfile.IsActive"/> = <see langword="true"/>).
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The active <see cref="CredentialProfile"/>, or <see langword="null"/> when no
    /// profile is currently active (e.g. on a fresh installation before any profile
    /// has been configured).
    /// </returns>
    Task<CredentialProfile?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new credential profile to the backing store.
    /// </summary>
    /// <param name="profile">
    /// The fully-populated <see cref="CredentialProfile"/> to insert.
    /// <see cref="CredentialProfile.Id"/> must be set to a new <see cref="Guid"/>
    /// before calling this method.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the profile has been committed.</returns>
    Task CreateAsync(CredentialProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing credential profile in the backing store.
    /// </summary>
    /// <param name="profile">
    /// The modified <see cref="CredentialProfile"/> to persist. The record is located
    /// by <see cref="CredentialProfile.Id"/>; all other fields are overwritten.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the update has been committed.</returns>
    Task UpdateAsync(CredentialProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes a credential profile by setting
    /// <see cref="CredentialProfile.IsDeleted"/> to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// If the profile being soft-deleted is currently active
    /// (<see cref="CredentialProfile.IsActive"/> = <see langword="true"/>),
    /// <c>IsActive</c> must also be cleared atomically in the same operation
    /// to prevent a deleted profile from remaining the active default.
    /// </remarks>
    /// <param name="id">
    /// The <see cref="CredentialProfile.Id"/> of the profile to soft-delete.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the soft-delete has been committed.</returns>
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the specified profile as the active default, atomically clearing
    /// <see cref="CredentialProfile.IsActive"/> on all other profiles.
    /// </summary>
    /// <remarks>
    /// This operation must be performed within a single database transaction to
    /// guarantee that the "at most one active profile" invariant is never violated,
    /// even under concurrent requests.
    /// </remarks>
    /// <param name="id">
    /// The <see cref="CredentialProfile.Id"/> of the profile to activate.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the activation has been committed.</returns>
    Task ActivateAsync(Guid id, CancellationToken cancellationToken);
}
