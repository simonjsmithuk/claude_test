using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="IUserPreferencesRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Shared-primary-key pattern:</strong> <see cref="UserPreference"/> uses
/// <see cref="UserPreference.UserId"/> as both the primary key and the foreign key
/// to <see cref="User"/>.  This enforces the one-to-one relationship at the database
/// level with no additional unique index required.  Because the PK is a
/// client-assigned value (<c>ValueGeneratedNever()</c> in
/// <c>UserPreferenceConfiguration</c>), EF Core cannot distinguish INSERT from
/// UPDATE based on the PK being zero/empty — both paths must be handled explicitly.
/// </para>
///
/// <para>
/// <strong>Upsert strategy:</strong> <see cref="UpsertAsync"/> checks whether the
/// row already exists using <c>AnyAsync</c>.  If the row exists, <c>Update()</c> is
/// called to attach the entity as <see cref="EntityState.Modified"/> and perform a
/// full-row UPDATE.  If the row does not exist, <c>AddAsync()</c> inserts it.  This
/// pattern is safe for the sequential-request use case; for high-concurrency
/// concurrent first-saves the database unique-key constraint acts as a final guard
/// (any concurrent INSERT that races to win will succeed; the loser will receive a
/// <see cref="DbUpdateException"/> from the FK/PK violation, which propagates to
/// the caller for the application layer to handle).
/// </para>
///
/// <para>
/// <strong>IMemoryCache with 5-minute TTL:</strong> <see cref="GetByUserIdAsync"/>
/// populates a per-user cache entry keyed by <see cref="UserPreference.UserId"/>.
/// <see cref="UpsertAsync"/> explicitly evicts the corresponding cache entry before
/// persisting and repopulates it with the freshly saved entity afterwards, ensuring
/// that subsequent reads serve the up-to-date preferences without an additional DB
/// round-trip.
/// </para>
///
/// <para>
/// <strong>Cache entry for null results:</strong> When a user has never saved
/// preferences, <see cref="GetByUserIdAsync"/> returns <see langword="null"/>.  The
/// null result is NOT cached: caching nulls would cause <see cref="UpsertAsync"/>
/// to require an explicit null-cache-entry eviction in addition to the regular key
/// eviction, complicating the logic without meaningful performance benefit (the
/// common hot path is users who have already saved preferences).
/// </para>
/// </remarks>
public sealed class UserPreferencesRepository : IUserPreferencesRepository
{
    // Cache-key prefix for per-user entries.  Concatenated with the UserId GUID
    // string to produce a unique, collision-free key for each user.
    // Using a typed prefix object avoids magic-string key collisions with other
    // cache users that share the same IMemoryCache instance.
    private const string CacheKeyPrefix = "UserPreferences_";

    // 5-minute absolute TTL as required by the acceptance criteria.
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserPreferencesRepository> _logger;

    /// <summary>
    /// Initialises the repository with the scoped <see cref="AppDbContext"/>,
    /// the application-wide <see cref="IMemoryCache"/>, and a structured logger.
    /// </summary>
    /// <param name="context">
    /// The EF Core database context for this unit of work (HTTP request scope).
    /// </param>
    /// <param name="cache">
    /// The in-process memory cache shared across the application.
    /// Per-user entries are stored with a 5-minute absolute TTL.
    /// Must be registered via <c>services.AddMemoryCache()</c> in the DI container.
    /// </param>
    /// <param name="logger">
    /// Structured logger for cache-hit / cache-miss / upsert diagnostic events.
    /// </param>
    public UserPreferencesRepository(
        AppDbContext context,
        IMemoryCache cache,
        ILogger<UserPreferencesRepository> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // GetByUserIdAsync — per-user read with 5-minute cache
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="null"/> when the user has never explicitly saved
    /// preferences.  The application layer is responsible for substituting
    /// application-layer defaults (e.g. <see cref="UserPreference.DefaultPageSize"/>)
    /// when <see langword="null"/> is returned.
    ///
    /// <para>
    /// Cache key is per-user (<c>"UserPreferences_{userId}"</c>) so that concurrent
    /// requests for different users each maintain independent TTL windows.
    /// </para>
    /// </remarks>
    public async Task<UserPreference?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(userId);

        // ── Cache hit ────────────────────────────────────────────────────────
        if (_cache.TryGetValue(cacheKey, out UserPreference? cached))
        {
            _logger.LogDebug(
                "UserPreferences cache HIT for UserId={UserId}",
                userId);
            return cached;
        }

        // ── Cache miss: query the database ───────────────────────────────────
        _logger.LogDebug(
            "UserPreferences cache MISS for UserId={UserId} — querying database",
            userId);

        // AsNoTracking: the result is read-only and will be placed in the cache.
        // A tracking query would anchor the entity to the Scoped DbContext, which
        // is disposed at request end — the cached reference would then hold a
        // stale, disposed-context entity.
        // FindAsync cannot be used here because it only checks the in-memory
        // change-tracker, not the database, for detached/AsNoTracking scenarios.
        var preferences = await _context.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(up => up.UserId == userId, cancellationToken);

        // Only cache non-null results: null means "no row exists yet" and will
        // become stale the moment the user calls UpsertAsync for the first time.
        // Caching nulls would require an additional eviction step in UpsertAsync
        // with no meaningful cache-efficiency benefit.
        if (preferences is not null)
        {
            using var entry = _cache.CreateEntry(cacheKey);
            entry.Value = preferences;
            entry.AbsoluteExpirationRelativeToNow = _cacheTtl;

            _logger.LogDebug(
                "UserPreferences cached for UserId={UserId} (TTL={Ttl} minutes)",
                userId,
                _cacheTtl.TotalMinutes);
        }
        else
        {
            _logger.LogDebug(
                "No UserPreferences row found for UserId={UserId} — "
                + "user has not yet saved preferences",
                userId);
        }

        return preferences;
    }

    // ────────────────────────────────────────────────────────────────────────
    // UpsertAsync — insert-or-update with cache eviction + repopulation
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <strong>Upsert mechanics:</strong> An <c>AnyAsync</c> existence check is
    /// performed first.  If the row exists, <c>Update()</c> attaches the entity as
    /// Modified (full-row UPDATE).  If not, <c>AddAsync()</c> inserts it.  This is
    /// safe for sequential requests.  For concurrent first-time saves the database
    /// PK constraint will reject the second INSERT with a
    /// <see cref="DbUpdateException"/> — this exception propagates to the caller
    /// rather than being swallowed, preserving the "no exception swallowing" contract.
    /// </para>
    ///
    /// <para>
    /// <strong>Cache lifecycle:</strong> The cache entry for <paramref name="preferences"/>'s
    /// <see cref="UserPreference.UserId"/> is removed before the DB write (evict-on-write).
    /// After a successful <c>SaveChangesAsync</c>, the saved entity is immediately
    /// re-stored in the cache so the next read within the TTL window is served from
    /// cache without an additional DB round-trip.
    /// </para>
    /// </remarks>
    public async Task UpsertAsync(
        UserPreference preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var cacheKey = BuildCacheKey(preferences.UserId);

        // ── Evict stale cache entry before the write ─────────────────────────
        // Remove happens before SaveChangesAsync so that any concurrent reader
        // arriving between eviction and the DB write incurs a DB hit (reading the
        // still-consistent pre-update DB row) rather than serving a stale cache value.
        _cache.Remove(cacheKey);

        // ── Determine INSERT vs UPDATE ────────────────────────────────────────
        // AnyAsync is used instead of FindAsync so that the existence check is
        // independent of the change-tracker state (the entity passed in may already
        // be detached, as is common when the caller constructs a new instance from
        // DTO data without loading the existing row first).
        var rowExists = await _context.UserPreferences
            .AnyAsync(up => up.UserId == preferences.UserId, cancellationToken);

        if (rowExists)
        {
            _logger.LogDebug(
                "UpsertAsync: existing UserPreferences row found for UserId={UserId} — updating",
                preferences.UserId);

            // Update() transitions the entity to Modified and will emit a full-row UPDATE.
            // This handles the detached entity case (entity constructed from DTO data)
            // by re-attaching it to the context's change-tracker before saving.
            _context.UserPreferences.Update(preferences);
        }
        else
        {
            _logger.LogDebug(
                "UpsertAsync: no UserPreferences row found for UserId={UserId} — inserting",
                preferences.UserId);

            // AddAsync queues the entity for INSERT.  The PK (UserId) is a client-assigned
            // Guid — EF Core will not attempt to use a sequence or identity column.
            await _context.UserPreferences.AddAsync(preferences, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "UserPreferences {Operation} successfully for UserId={UserId}",
            rowExists ? "updated" : "inserted",
            preferences.UserId);

        // ── Re-populate cache with the freshly persisted entity ──────────────
        // The saved entity is cached immediately so the next GetByUserIdAsync call
        // within the TTL window is served without a DB round-trip.
        using var cacheEntry = _cache.CreateEntry(cacheKey);
        cacheEntry.Value = preferences;
        cacheEntry.AbsoluteExpirationRelativeToNow = _cacheTtl;

        _logger.LogDebug(
            "UserPreferences cache repopulated for UserId={UserId} after upsert (TTL={Ttl} minutes)",
            preferences.UserId,
            _cacheTtl.TotalMinutes);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the per-user cache key by combining the fixed prefix with the
    /// <paramref name="userId"/> string representation.
    /// </summary>
    /// <param name="userId">The <see cref="User.Id"/> of the target user.</param>
    /// <returns>
    /// A string of the form <c>"UserPreferences_{userId}"</c> — unique per user
    /// and stable across the application lifetime.
    /// </returns>
    private static string BuildCacheKey(Guid userId) =>
        $"{CacheKeyPrefix}{userId}";
}
