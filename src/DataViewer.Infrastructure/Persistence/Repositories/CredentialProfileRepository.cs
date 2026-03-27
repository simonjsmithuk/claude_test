using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="ICredentialProfileRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Soft-delete semantics:</strong> Records are never physically removed.
/// <see cref="SoftDeleteAsync"/> sets <see cref="CredentialProfile.IsDeleted"/> to
/// <see langword="true"/> and refreshes <see cref="CredentialProfile.UpdatedAt"/>.
/// All query methods filter out deleted rows by default (unless
/// <c>includeDeleted = true</c> is explicitly requested) so that soft-deleted
/// profiles are invisible to normal application flows while remaining accessible
/// for audit-log reconciliation.
/// </para>
///
/// <para>
/// <strong>Single-active-profile invariant:</strong> At most one profile may carry
/// <see cref="CredentialProfile.IsActive"/> = <see langword="true"/> at any time.
/// <see cref="ActivateAsync"/> enforces this atomically inside a database transaction:
/// it first clears <c>IsActive</c> on all non-deleted profiles using
/// <c>ExecuteUpdateAsync</c>, then sets <c>IsActive = true</c> on the target profile —
/// all within the same transaction. This prevents a window where zero or two profiles
/// are simultaneously active under concurrent requests.
/// </para>
///
/// <para>
/// <strong>Name-uniqueness enforcement:</strong> <see cref="CreateAsync"/> performs an
/// explicit application-level uniqueness check against non-deleted profiles before
/// issuing the <c>INSERT</c> and throws <see cref="DuplicateNameException"/> on a
/// conflict. This surfaces a clean, typed domain exception rather than relying on
/// the provider-specific <c>DbUpdateException</c> from the database unique index —
/// keeping error handling portable across PostgreSQL and MySQL (Product Spec G-05).
/// The database partial-unique index on <c>Name WHERE IsDeleted = false</c> is
/// retained as a last line of defence (race-condition guard), but the application
/// check is the primary enforcement point.
/// </para>
///
/// <para>
/// <strong>Targeted column updates via <c>ExecuteUpdateAsync</c>:</strong>
/// <see cref="SoftDeleteAsync"/> and <see cref="ActivateAsync"/> use EF Core 7+
/// bulk-update APIs to issue a single <c>UPDATE … SET …</c> statement without first
/// loading the entity into memory. This approach is inherently atomic at the database
/// level and avoids the load-then-save anti-pattern for the targeted field changes
/// these operations require.
/// </para>
///
/// <para>
/// <strong>No raw SQL:</strong> All queries and mutations are expressed exclusively
/// via EF Core LINQ operators and the <c>ExecuteUpdateAsync</c> bulk-operation API.
/// </para>
/// </remarks>
public sealed class CredentialProfileRepository : ICredentialProfileRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<CredentialProfileRepository> _logger;

    /// <summary>
    /// Initialises the repository with the scoped <see cref="AppDbContext"/>
    /// and a logger for diagnostic output.
    /// </summary>
    /// <param name="context">
    /// The EF Core database context for this unit of work (HTTP request scope).
    /// </param>
    /// <param name="logger">
    /// Structured logger used to record diagnostic events such as not-found
    /// conditions, name-conflict rejections, and activation state changes.
    /// </param>
    public CredentialProfileRepository(
        AppDbContext context,
        ILogger<CredentialProfileRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ── Query methods ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <see cref="DbSet{TEntity}.FindAsync(object[], CancellationToken)"/> which
    /// checks the EF Core change-tracker cache first, then hits the database — optimal
    /// for the common pattern of loading a profile that was already fetched earlier in
    /// the same request scope.
    /// <para>
    /// Soft-deleted profiles ARE returned by this method so that the caller can
    /// distinguish "profile exists but is deleted" from "profile does not exist at all"
    /// when needed (e.g. for audit-log reconciliation). If the caller requires
    /// non-deleted-only semantics, it must inspect <see cref="CredentialProfile.IsDeleted"/>
    /// on the returned entity.
    /// </para>
    /// </remarks>
    public async Task<CredentialProfile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching credential profile by ID {ProfileId}", id);

        // FindAsync checks the change-tracker cache before issuing a query — efficient
        // when the same profile was already loaded earlier in this request scope.
        var profile = await _context.CredentialProfiles
            .FindAsync([id], cancellationToken);

        if (profile is null)
            _logger.LogDebug("Credential profile {ProfileId} not found", id);

        return profile;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Results are ordered by <see cref="CredentialProfile.CreatedAt"/> ascending so
    /// that the caller receives profiles in creation order — a stable, deterministic
    /// ordering that matches the typical "list" UI presentation.
    /// </remarks>
    public async Task<IReadOnlyList<CredentialProfile>> GetAllAsync(
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Fetching all credential profiles (includeDeleted={IncludeDeleted})",
            includeDeleted);

        // AsNoTracking: the returned list is read-only; no mutations will be tracked.
        var query = _context.CredentialProfiles
            .AsNoTracking();

        // Default behaviour (Acceptance Criteria): filter out IsDeleted=true unless
        // the caller explicitly opts in.
        if (!includeDeleted)
            query = query.Where(cp => !cp.IsDeleted);

        var profiles = await query
            .OrderBy(cp => cp.CreatedAt)
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "Retrieved {Count} credential profile(s) (includeDeleted={IncludeDeleted})",
            profiles.Count,
            includeDeleted);

        return profiles.AsReadOnly();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Soft-deleted profiles are explicitly excluded: a soft-deleted profile must
    /// never be considered the active default, even if its <c>IsActive</c> flag was
    /// not cleared atomically during the soft-delete operation (defensive guard).
    /// <para>
    /// <c>SingleOrDefaultAsync</c> is preferred over <c>FirstOrDefaultAsync</c> to
    /// loudly surface an accidental breach of the single-active-profile invariant
    /// (i.e. more than one profile has <c>IsActive = true</c>) as an
    /// <see cref="InvalidOperationException"/> rather than silently returning an
    /// arbitrary row.
    /// </para>
    /// </remarks>
    public async Task<CredentialProfile?> GetActiveAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching active credential profile");

        // SingleOrDefaultAsync will throw InvalidOperationException if the invariant
        // is violated (more than one active non-deleted profile exists). This is the
        // correct loud-failure behaviour — the invariant must never be silently bypassed.
        var profile = await _context.CredentialProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cp => cp.IsActive && !cp.IsDeleted,
                cancellationToken);

        if (profile is null)
            _logger.LogDebug("No active credential profile found");
        else
            _logger.LogDebug("Active credential profile is {ProfileId} ('{ProfileName}')",
                profile.Id, profile.Name);

        return profile;
    }

    // ── Mutation methods ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// An explicit application-level uniqueness check is performed against
    /// non-deleted profiles before the <c>INSERT</c> to surface a typed
    /// <see cref="DuplicateNameException"/> rather than a provider-specific
    /// <c>DbUpdateException</c>. The database partial-unique index (Name WHERE
    /// IsDeleted = false) is retained as a final guard against race conditions
    /// but must not be the primary enforcement mechanism.
    /// </para>
    /// <para>
    /// <see cref="CredentialProfile.CreatedAt"/> and
    /// <see cref="CredentialProfile.UpdatedAt"/> are set to
    /// <see cref="DateTime.UtcNow"/> here. The
    /// <see cref="AppDbContext.EnforceUtcDateTimes"/> interceptor ensures the
    /// <see cref="DateTimeKind"/> is correct for both providers.
    /// </para>
    /// </remarks>
    public async Task CreateAsync(
        CredentialProfile profile,
        CancellationToken cancellationToken)
    {
        // Application-level name uniqueness check against non-deleted profiles.
        // Throws DuplicateNameException before hitting the database if a conflict
        // exists, giving callers a clean typed domain exception to handle.
        await ThrowIfNameTakenAsync(profile.Name, excludeId: null, cancellationToken);

        // Stamp timestamps as UTC. AppDbContext.EnforceUtcDateTimes normalises the
        // DateTimeKind before SaveChangesAsync so both Npgsql and Pomelo accept them.
        var utcNow = DateTime.UtcNow;
        profile.CreatedAt = utcNow;
        profile.UpdatedAt = utcNow;

        _logger.LogInformation(
            "Creating credential profile '{ProfileName}' (ID={ProfileId}) for user {UserId}",
            profile.Name, profile.Id, profile.CreatedByUserId);

        await _context.CredentialProfiles.AddAsync(profile, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses EF Core change tracking: if <paramref name="profile"/> is already tracked
    /// by the current context scope, <c>Update</c> is a no-op on the tracking state.
    /// If the entity arrived from a no-tracking query, <c>Update</c> attaches it and
    /// marks all scalar properties as <c>Modified</c>, causing a full-row
    /// <c>UPDATE</c> statement.
    /// <para>
    /// <see cref="CredentialProfile.UpdatedAt"/> is refreshed to
    /// <see cref="DateTime.UtcNow"/> on every update so that the audit trail
    /// always reflects the last write time regardless of how the entity was loaded.
    /// </para>
    /// </remarks>
    public async Task UpdateAsync(
        CredentialProfile profile,
        CancellationToken cancellationToken)
    {
        profile.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Updating credential profile {ProfileId} ('{ProfileName}')",
            profile.Id, profile.Name);

        // Update() attaches the entity if not already tracked and marks it Modified.
        _context.CredentialProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Uses <c>ExecuteUpdateAsync</c> to issue a single targeted
    /// <c>UPDATE CredentialProfiles SET IsDeleted=1, IsActive=0, UpdatedAt=@utcNow WHERE Id=@id</c>
    /// statement without loading the entity. The simultaneous clearing of
    /// <c>IsActive</c> is required by the interface contract: a soft-deleted profile
    /// must never remain the active default.
    /// </para>
    /// <para>
    /// Throws <see cref="NotFoundException"/> when no row with the given
    /// <paramref name="id"/> exists, making the not-found case explicit so the API
    /// layer can return <c>404 Not Found</c> rather than silently succeeding on a
    /// ghost update.
    /// </para>
    /// </remarks>
    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Soft-deleting credential profile {ProfileId}", id);

        var utcNow = DateTime.UtcNow;

        // ExecuteUpdateAsync translates the setter lambda to a single parameterised UPDATE.
        // IsActive is cleared atomically in the same statement to satisfy the interface
        // contract: a soft-deleted profile must not remain the active default.
        var affected = await _context.CredentialProfiles
            .Where(cp => cp.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(cp => cp.IsDeleted, true)
                    .SetProperty(cp => cp.IsActive, false)
                    .SetProperty(cp => cp.UpdatedAt, utcNow),
                cancellationToken);

        ThrowIfNotFound(affected, id, nameof(SoftDeleteAsync));

        _logger.LogInformation(
            "Credential profile {ProfileId} soft-deleted successfully", id);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The "at most one active profile" invariant is enforced atomically within a
    /// single database transaction:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       Verify that the target profile exists and is not soft-deleted.
    ///       Throws <see cref="NotFoundException"/> if the profile cannot be
    ///       found or has already been soft-deleted, so the caller receives a
    ///       clear error rather than silently activating a deleted profile.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Clear <c>IsActive</c> on ALL non-deleted profiles (including the
    ///       target) using a bulk <c>ExecuteUpdateAsync</c> — a single round-trip.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Set <c>IsActive = true</c> on the target profile using a second
    ///       targeted <c>ExecuteUpdateAsync</c>.
    ///     </description>
    ///   </item>
    /// </list>
    /// Both <c>ExecuteUpdateAsync</c> calls participate in the same
    /// <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/>
    /// so that no intermediate state is ever committed to the database.
    /// </para>
    /// </remarks>
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Activating credential profile {ProfileId}", id);

        // Verify the target profile exists and is not soft-deleted before opening
        // a transaction, so we fail fast with a clear NotFoundException rather than
        // committing a transaction that ends with zero rows activated.
        var exists = await _context.CredentialProfiles
            .AnyAsync(cp => cp.Id == id && !cp.IsDeleted, cancellationToken);

        if (!exists)
        {
            _logger.LogWarning(
                "ActivateAsync: credential profile {ProfileId} not found or is soft-deleted",
                id);

            throw new NotFoundException("CredentialProfile", id.ToString());
        }

        var utcNow = DateTime.UtcNow;

        // Open a transaction to guarantee that the deactivate-all / activate-one
        // pair is committed atomically. If either step fails the entire transaction
        // is rolled back, preventing the database from entering a state where zero
        // or two profiles are simultaneously active.
        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            // Step 1: Clear IsActive on all non-deleted profiles (including target).
            // A single bulk UPDATE with no per-entity round-trips.
            await _context.CredentialProfiles
                .Where(cp => !cp.IsDeleted)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(cp => cp.IsActive, false)
                        .SetProperty(cp => cp.UpdatedAt, utcNow),
                    cancellationToken);

            // Step 2: Set IsActive = true on the target profile only.
            await _context.CredentialProfiles
                .Where(cp => cp.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(cp => cp.IsActive, true)
                        .SetProperty(cp => cp.UpdatedAt, utcNow),
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Credential profile {ProfileId} is now the active profile", id);
        }
        catch (Exception ex)
        {
            // Roll back on any failure — the invariant must not be left in a broken
            // intermediate state (e.g. all profiles deactivated but none re-activated).
            _logger.LogError(
                ex,
                "ActivateAsync: transaction failed for profile {ProfileId} — rolling back",
                id);

            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether a non-deleted <see cref="CredentialProfile"/> with the given
    /// <paramref name="name"/> already exists and throws
    /// <see cref="DuplicateNameException"/> if so.
    /// </summary>
    /// <param name="name">
    /// The proposed profile name to check for uniqueness.
    /// </param>
    /// <param name="excludeId">
    /// When provided, the profile with this ID is excluded from the check — used
    /// for rename operations where the current row should not conflict with itself.
    /// Pass <see langword="null"/> for new-create operations.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <exception cref="DuplicateNameException">
    /// Thrown when a non-deleted profile with the given <paramref name="name"/> exists.
    /// </exception>
    private async Task ThrowIfNameTakenAsync(
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var query = _context.CredentialProfiles
            .Where(cp => !cp.IsDeleted && cp.Name == name);

        // Exclude the profile being updated from the uniqueness check so that a
        // no-op rename (same name) or an update of other fields does not reject itself.
        if (excludeId.HasValue)
            query = query.Where(cp => cp.Id != excludeId.Value);

        var isDuplicate = await query.AnyAsync(cancellationToken);

        if (isDuplicate)
        {
            _logger.LogWarning(
                "Credential profile name '{ProfileName}' is already in use by a non-deleted profile",
                name);

            throw new DuplicateNameException("CredentialProfile", name);
        }
    }

    /// <summary>
    /// Throws <see cref="NotFoundException"/> when a bulk-update operation affected
    /// zero rows, indicating that the target profile does not exist in the database.
    /// </summary>
    /// <param name="affectedRows">
    /// The number of rows updated, as returned by <c>ExecuteUpdateAsync</c>.
    /// </param>
    /// <param name="profileId">
    /// The profile ID that was targeted — included in the exception payload.
    /// </param>
    /// <param name="callerName">
    /// The name of the calling method — used in the diagnostic log entry only,
    /// not exposed to external callers.
    /// </param>
    /// <exception cref="NotFoundException">
    /// Thrown when <paramref name="affectedRows"/> is zero.
    /// </exception>
    private void ThrowIfNotFound(int affectedRows, Guid profileId, string callerName)
    {
        if (affectedRows == 0)
        {
            _logger.LogWarning(
                "{CallerName}: credential profile {ProfileId} not found — no rows were updated",
                callerName,
                profileId);

            throw new NotFoundException("CredentialProfile", profileId.ToString());
        }
    }
}
