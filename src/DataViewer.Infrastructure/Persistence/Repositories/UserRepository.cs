using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="IUserRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// All mutation operations fall into two categories:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <strong>Full-entity updates</strong> — <see cref="CreateAsync"/> and
///       <see cref="UpdateAsync"/> use EF Core change tracking. The entity is
///       attached to (or already tracked by) the context and then saved with
///       <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Targeted column updates</strong> — <see cref="IncrementFailedLoginAsync"/>,
///       <see cref="ResetFailedLoginAsync"/>, <see cref="LockAsync"/>, and
///       <see cref="UnlockAsync"/> use EF Core's <c>ExecuteUpdateAsync</c>
///       (introduced in EF Core 7.0) to issue a single <c>UPDATE … SET …</c>
///       statement without first loading the entity. This approach is inherently
///       atomic at the database level and prevents the lost-update race conditions
///       that a load-then-save pattern would introduce for concurrent login attempts.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// <strong>Case-insensitive username lookup:</strong> The <see cref="UserConfiguration"/>
/// documents that application code is responsible for normalising <c>UserName</c> to
/// lower-case before every read and write. <see cref="GetByUsernameAsync"/> therefore
/// compares against <c>username.ToLowerInvariant()</c>, which matches the lower-case
/// values stored in the database. No provider-specific collation annotations are used,
/// keeping the implementation portable across PostgreSQL and MySQL (Product Spec G-05).
/// </para>
///
/// <para>
/// <strong>No raw SQL:</strong> All queries and mutations are expressed exclusively
/// via EF Core LINQ operators and the <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c>
/// bulk-operation API. <c>ExecuteUpdateAsync</c> translates the lambda expression to
/// parameterised SQL internally — it is an EF LINQ operation, not raw SQL.
/// </para>
/// </remarks>
public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserRepository> _logger;

    /// <summary>
    /// Initialises the repository with the scoped <see cref="AppDbContext"/>
    /// and a logger for diagnostic output.
    /// </summary>
    /// <param name="context">
    /// The EF Core database context for this unit of work (HTTP request scope).
    /// </param>
    /// <param name="logger">
    /// Structured logger used to record diagnostic events such as not-found
    /// conditions and lockout state changes.
    /// </param>
    public UserRepository(AppDbContext context, ILogger<UserRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ── Query methods ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <see cref="DbSet{TEntity}.FindAsync(object[], CancellationToken)"/> which
    /// checks the change-tracker cache first, then hits the database — optimal for
    /// the common pattern of loading a user that was already fetched earlier in the
    /// same request scope.
    /// </remarks>
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching user by ID {UserId}", id);

        // FindAsync checks the first-level change-tracker cache before issuing a query.
        var user = await _context.Users.FindAsync([id], cancellationToken);

        if (user is null)
            _logger.LogDebug("User {UserId} not found", id);

        return user;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Comparison is performed against the lower-case normalised form of
    /// <paramref name="username"/> — matching the storage convention established in
    /// <see cref="CreateAsync"/> and documented in <see cref="UserConfiguration"/>.
    /// This approach achieves case-insensitive lookup without requiring a
    /// provider-specific collation annotation, keeping the implementation portable
    /// across PostgreSQL (case-sensitive by default) and MySQL.
    /// </para>
    /// <para>
    /// <c>SingleOrDefaultAsync</c> is used instead of <c>FirstOrDefaultAsync</c> to
    /// surface an accidental uniqueness-constraint violation as a loud
    /// <see cref="InvalidOperationException"/> rather than silently returning the
    /// first of multiple matching rows.
    /// </para>
    /// </remarks>
    public async Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        // Normalise to lower-case to match the stored value convention.
        var normalised = username.ToLowerInvariant();

        _logger.LogDebug("Fetching user by username (normalised: '{NormalisedUsername}')", normalised);

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserName == normalised, cancellationToken);

        if (user is null)
            _logger.LogDebug("User with username '{NormalisedUsername}' not found", normalised);

        return user;
    }

    // ── Mutation methods (full-entity) ────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <see cref="User.UserName"/> and <see cref="User.Email"/> are lower-cased
    /// before insertion so that the unique index on <c>UserName</c> and the
    /// lookup-by-email path both operate on a consistent normalised form.
    /// </para>
    /// <para>
    /// The caller is responsible for setting <see cref="User.PasswordHash"/> to a
    /// valid bcrypt hash before invoking this method — the raw password must never
    /// reach the repository layer.
    /// </para>
    /// </remarks>
    public async Task CreateAsync(User user, CancellationToken cancellationToken)
    {
        // ASSUMPTION: The caller has already validated that UserName is unique and
        //             that PasswordHash contains a bcrypt hash, not a plain-text password.

        // Enforce lower-case storage convention for username and email so that
        // GetByUsernameAsync lookups are always consistent.
        user.UserName = user.UserName.ToLowerInvariant();
        user.Email = user.Email.ToLowerInvariant();

        _logger.LogInformation(
            "Creating user account for username '{UserName}' with role {Role}",
            user.UserName,
            user.Role);

        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Uses EF Core change tracking: if <paramref name="user"/> is already tracked
    /// by the current context scope, <c>Update</c> is a no-op on the tracking state
    /// (the entity is already <c>Modified</c>). If the entity arrived from a
    /// no-tracking query (e.g. <see cref="GetByUsernameAsync"/>) or was detached,
    /// <c>Update</c> attaches it and marks the full entity as <c>Modified</c>,
    /// which causes all scalar properties to be included in the generated
    /// <c>UPDATE</c> statement.
    /// </para>
    /// <para>
    /// Security-sensitive fields (<see cref="User.FailedLoginCount"/>,
    /// <see cref="User.IsLocked"/>, <see cref="User.LockoutUntil"/>) should be
    /// mutated exclusively through the dedicated methods on this interface rather
    /// than through this general-purpose update, as documented on
    /// <see cref="IUserRepository.UpdateAsync"/>.
    /// </para>
    /// </remarks>
    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        // Normalise mutable identity fields to maintain the lower-case storage convention.
        user.UserName = user.UserName.ToLowerInvariant();
        user.Email = user.Email.ToLowerInvariant();

        _logger.LogInformation("Updating user account {UserId}", user.Id);

        // Update() attaches the entity if not already tracked and marks it Modified.
        // If it is already tracked in the same scope, EF will detect changes via
        // its snapshot-based change detection and only emit a no-op if nothing changed.
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Mutation methods (targeted / atomic column updates) ───────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <c>ExecuteUpdateAsync</c> to issue a single
    /// <c>UPDATE Users SET FailedLoginCount = FailedLoginCount + 1 WHERE Id = @id</c>
    /// statement without loading the entity. The database-level increment is inherently
    /// atomic under the default READ COMMITTED isolation level, preventing the
    /// lost-update race condition that a load-then-increment-then-save pattern would
    /// introduce when multiple concurrent failed-login requests arrive simultaneously
    /// for the same account.
    ///
    /// <para>
    /// Throws <see cref="NotFoundException"/> when no row with the given
    /// <paramref name="userId"/> exists, making the not-found case explicit and
    /// allowing the caller to surface an appropriate error rather than silently
    /// succeeding on a ghost update.
    /// </para>
    /// </remarks>
    public async Task IncrementFailedLoginAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Incrementing FailedLoginCount for user {UserId}", userId);

        // ExecuteUpdateAsync translates the setter lambda to parameterised SQL:
        //   UPDATE Users SET FailedLoginCount = FailedLoginCount + 1 WHERE Id = @p0
        // No entity is loaded; the update is issued as a single round-trip.
        var affected = await _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    u => u.FailedLoginCount,
                    u => u.FailedLoginCount + 1),
                cancellationToken);

        ThrowIfNotFound(affected, userId, nameof(IncrementFailedLoginAsync));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Issues a single targeted <c>UPDATE</c> that atomically resets
    /// <see cref="User.FailedLoginCount"/> to zero and clears
    /// <see cref="User.LockoutUntil"/> (setting it to <see langword="null"/>).
    /// <see cref="User.IsLocked"/> is deliberately NOT cleared here — unlocking
    /// the account is a separate, intentional administrative action performed
    /// via <see cref="UnlockAsync"/>. This mirrors the domain method
    /// <see cref="User.RecordSuccessfulLogin"/> which only resets the counter and
    /// last-login timestamp.
    ///
    /// <para>
    /// Throws <see cref="NotFoundException"/> when no row with the given
    /// <paramref name="userId"/> exists.
    /// </para>
    /// </remarks>
    public async Task ResetFailedLoginAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resetting FailedLoginCount for user {UserId}", userId);

        // Reset the counter and clear any pending lockout expiry atomically.
        // IsLocked is intentionally left unchanged — full unlock goes through UnlockAsync.
        var affected = await _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.LockoutUntil, (DateTime?)null),
                cancellationToken);

        ThrowIfNotFound(affected, userId, nameof(ResetFailedLoginAsync));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Issues a single targeted <c>UPDATE</c> that atomically sets
    /// <see cref="User.IsLocked"/> to <see langword="true"/> and records the
    /// optional lockout expiry in <see cref="User.LockoutUntil"/>.
    ///
    /// <para>
    /// The caller (application layer) is responsible for computing the
    /// <paramref name="lockoutUntil"/> value as <c>DateTime.UtcNow + lockoutDuration</c>
    /// where <c>lockoutDuration</c> is read from the <c>Auth:LockoutDurationMinutes</c>
    /// configuration key. Passing <see langword="null"/> produces a permanent
    /// administrative lock with no expiry (see <see cref="User.Lock"/>).
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="NotFoundException"/> when no row with the given
    /// <paramref name="userId"/> exists.
    /// </para>
    /// </remarks>
    public async Task LockAsync(
        Guid userId,
        DateTime? lockoutUntil,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Locking user {UserId}. LockoutUntil={LockoutUntil}",
            userId,
            lockoutUntil?.ToString("O") ?? "<permanent>");

        var affected = await _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.IsLocked, true)
                    .SetProperty(u => u.LockoutUntil, lockoutUntil),
                cancellationToken);

        ThrowIfNotFound(affected, userId, nameof(LockAsync));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Issues a single targeted <c>UPDATE</c> that atomically clears all three
    /// lockout-related fields — <see cref="User.IsLocked"/>,
    /// <see cref="User.LockoutUntil"/>, and <see cref="User.FailedLoginCount"/> —
    /// mirroring the atomicity guarantee of the domain method <see cref="User.Unlock"/>.
    ///
    /// <para>
    /// Throws <see cref="NotFoundException"/> when no row with the given
    /// <paramref name="userId"/> exists.
    /// </para>
    /// </remarks>
    public async Task UnlockAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Unlocking user account {UserId}", userId);

        var affected = await _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.IsLocked, false)
                    .SetProperty(u => u.LockoutUntil, (DateTime?)null)
                    .SetProperty(u => u.FailedLoginCount, 0),
                cancellationToken);

        ThrowIfNotFound(affected, userId, nameof(UnlockAsync));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Throws <see cref="NotFoundException"/> when a bulk-update operation affected
    /// zero rows, indicating that the target user does not exist in the database.
    /// </summary>
    /// <param name="affectedRows">
    /// The number of rows updated, as returned by <c>ExecuteUpdateAsync</c>.
    /// </param>
    /// <param name="userId">
    /// The user ID that was targeted — included in the exception payload.
    /// </param>
    /// <param name="callerName">
    /// The name of the calling method — used only in the diagnostic log entry,
    /// not exposed to external callers.
    /// </param>
    /// <exception cref="NotFoundException">
    /// Thrown when <paramref name="affectedRows"/> is zero.
    /// </exception>
    private void ThrowIfNotFound(int affectedRows, Guid userId, string callerName)
    {
        if (affectedRows == 0)
        {
            _logger.LogWarning(
                "{CallerName}: user {UserId} not found — no rows were updated",
                callerName,
                userId);

            throw new NotFoundException("User", userId.ToString());
        }
    }
}
