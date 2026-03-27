namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;

/// <summary>
/// Provides persistence and retrieval operations for <see cref="User"/> entities.
/// </summary>
/// <remarks>
/// Authentication state mutations (failed login tracking, lockout) are expressed as
/// dedicated fine-grained methods rather than a generic <c>UpdateAsync</c> to make
/// intent explicit, prevent accidental full-entity overwrites of security-sensitive
/// fields, and enable optimistic concurrency on the individual columns.
///
/// <para>
/// All lookup methods return <see langword="null"/> rather than throwing when the
/// requested entity does not exist, allowing the application layer to distinguish
/// between "not found" (return 404) and unexpected data errors (throw).
/// </para>
/// </remarks>
public interface IUserRepository
{
    /// <summary>
    /// Retrieves a single user by their primary key.
    /// </summary>
    /// <param name="id">The <see cref="User.Id"/> to look up.</param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The matching <see cref="User"/>, or <see langword="null"/> when no user
    /// with the specified <paramref name="id"/> exists.
    /// </returns>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a single user by their unique username (case-insensitive).
    /// </summary>
    /// <param name="username">
    /// The <see cref="User.UserName"/> to look up. Comparison is case-insensitive;
    /// implementations should use the database collation or a normalised form.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// The matching <see cref="User"/>, or <see langword="null"/> when no user
    /// with the specified <paramref name="username"/> exists.
    /// </returns>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new user account to the backing store.
    /// </summary>
    /// <param name="user">
    /// The fully-populated <see cref="User"/> to insert.
    /// <see cref="User.PasswordHash"/> must already be set to a bcrypt hash before
    /// calling this method — the raw password must never be passed or stored.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the user has been committed.</returns>
    Task CreateAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing user's non-security fields (e.g. email, role).
    /// </summary>
    /// <param name="user">
    /// The modified <see cref="User"/> to persist. The record is located by
    /// <see cref="User.Id"/>; all updatable fields are overwritten.
    /// Security-sensitive state (<see cref="User.FailedLoginCount"/>, <see cref="User.IsLocked"/>,
    /// <see cref="User.LockoutUntil"/>) should be mutated via the dedicated methods
    /// on this interface rather than through this general-purpose update.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the update has been committed.</returns>
    Task UpdateAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically increments <see cref="User.FailedLoginCount"/> by 1 for the
    /// specified user.
    /// </summary>
    /// <remarks>
    /// Uses a targeted SQL update (<c>SET FailedLoginCount = FailedLoginCount + 1</c>)
    /// rather than a full entity save to prevent race conditions when concurrent
    /// failed-login attempts arrive simultaneously.
    /// </remarks>
    /// <param name="userId">The <see cref="User.Id"/> of the user to update.</param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the increment has been committed.</returns>
    Task IncrementFailedLoginAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Resets <see cref="User.FailedLoginCount"/> to zero for the specified user,
    /// typically called after a successful authentication.
    /// </summary>
    /// <param name="userId">The <see cref="User.Id"/> of the user to update.</param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the reset has been committed.</returns>
    Task ResetFailedLoginAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the specified user account by setting <see cref="User.IsLocked"/> to
    /// <see langword="true"/> and recording the optional lockout expiry.
    /// </summary>
    /// <param name="userId">The <see cref="User.Id"/> of the user to lock.</param>
    /// <param name="lockoutUntil">
    /// UTC expiry of the lockout, or <see langword="null"/> for a permanent
    /// administrative lock with no expiry.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the lock has been committed.</returns>
    Task LockAsync(Guid userId, DateTime? lockoutUntil, CancellationToken cancellationToken);

    /// <summary>
    /// Unlocks the specified user account by clearing <see cref="User.IsLocked"/>,
    /// <see cref="User.LockoutUntil"/>, and resetting <see cref="User.FailedLoginCount"/>
    /// to zero atomically.
    /// </summary>
    /// <param name="userId">The <see cref="User.Id"/> of the user to unlock.</param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the unlock has been committed.</returns>
    Task UnlockAsync(Guid userId, CancellationToken cancellationToken);
}
