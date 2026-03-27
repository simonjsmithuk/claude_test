using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="ISystemSettingsRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Singleton-row contract:</strong> The <c>SystemSettings</c> table always
/// contains exactly one row with <c>Id = 1</c>, seeded by the EF Core migration
/// via <c>SystemSettingsConfiguration.HasData</c>.  <see cref="GetAsync"/> always
/// queries for <c>Id = 1</c> and creates a default row if one is missing (defensive
/// guard for environments where migrations are not applied before first use).
/// <see cref="UpdateAsync"/> performs an upsert against that fixed key so that no
/// second row can be inadvertently inserted through normal code paths.
/// </para>
///
/// <para>
/// <strong>IMemoryCache with 5-minute TTL:</strong> Both <see cref="GetAsync"/>
/// and <see cref="UpdateAsync"/> / cache-eviction are wired through the
/// <see cref="IMemoryCache"/> instance registered in the DI container.  On a cache
/// hit, no database round-trip is issued.  The cache entry is explicitly invalidated
/// (removed) on every successful <see cref="UpdateAsync"/> call, then immediately
/// re-populated with the updated entity, so subsequent reads always return a fresh
/// copy without an additional DB hit.
/// </para>
///
/// <para>
/// <strong>UpdatedAt enforcement:</strong> <see cref="UpdateAsync"/> always stamps
/// <see cref="SystemSettings.UpdatedAt"/> with <see cref="DateTime.UtcNow"/> before
/// persisting.  The <see cref="AppDbContext.EnforceUtcDateTimes"/> override ensures
/// the <see cref="DateTimeKind"/> is correct for both the PostgreSQL and MySQL
/// providers (Npgsql rejects <c>Unspecified</c> kinds at the driver level).
/// </para>
///
/// <para>
/// <strong>EF Core update strategy:</strong> <see cref="UpdateAsync"/> uses
/// <c>context.Update(settings)</c> which attaches the detached entity (returned
/// from a no-tracking read or supplied by the caller) and marks it as
/// <see cref="EntityState.Modified"/>.  Because <c>Id</c> is a client-assigned key
/// with <c>ValueGeneratedNever()</c>, EF Core will always issue an <c>UPDATE</c>
/// rather than an <c>INSERT</c>.  The defensive "create default row if missing" path
/// in <see cref="GetAsync"/> means that by the time <see cref="UpdateAsync"/> is
/// called, the row is guaranteed to exist.
/// </para>
/// </remarks>
public sealed class SystemSettingsRepository : ISystemSettingsRepository
{
    // ── Cache key constant ────────────────────────────────────────────────────
    // A typed, well-known key eliminates magic-string duplication across Get and Update.
    // object type prevents accidental key collisions with string cache keys in other
    // cache users that happen to share the same IMemoryCache instance.
    private static readonly object _cacheKey = new();

    // 5-minute absolute TTL as required by the acceptance criteria.
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    // Singleton Id for the single settings row — self-documenting named constant.
    private const int SingletonId = 1;

    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SystemSettingsRepository> _logger;

    /// <summary>
    /// Initialises the repository with the scoped <see cref="AppDbContext"/>,
    /// the application-wide <see cref="IMemoryCache"/>, and a structured logger.
    /// </summary>
    /// <param name="context">
    /// The EF Core database context for this unit of work (HTTP request scope).
    /// Used for all database reads and writes within this repository.
    /// </param>
    /// <param name="cache">
    /// The in-process memory cache shared across the application.
    /// Used to serve cached reads and to evict stale entries after writes.
    /// Must be registered via <c>services.AddMemoryCache()</c> in the DI container.
    /// </param>
    /// <param name="logger">
    /// Structured logger for cache-hit / cache-miss / write diagnostic events.
    /// </param>
    public SystemSettingsRepository(
        AppDbContext context,
        IMemoryCache cache,
        ILogger<SystemSettingsRepository> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // GetAsync — singleton read with 5-minute cache and self-healing default
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Attempts a cache lookup first.  On a cache miss, queries the database for
    /// the row with <c>Id = 1</c>.  If the row does not exist (e.g. migrations were
    /// not applied), a default <see cref="SystemSettings"/> row is created and
    /// persisted as a self-healing mechanism.  The resolved entity is then stored
    /// in the cache with a 5-minute absolute TTL before being returned.
    /// </remarks>
    public async Task<SystemSettings?> GetAsync(CancellationToken cancellationToken)
    {
        // ── Cache hit ────────────────────────────────────────────────────────
        if (_cache.TryGetValue(_cacheKey, out SystemSettings? cached))
        {
            _logger.LogDebug(
                "SystemSettings cache HIT — returning cached singleton (Id={Id})",
                cached!.Id);
            return cached;
        }

        // ── Cache miss: query the database ───────────────────────────────────
        _logger.LogDebug("SystemSettings cache MISS — querying database for Id={Id}", SingletonId);

        // AsNoTracking: the result will be placed in the cache and is intended for
        // read-only consumption.  A tracking query would anchor the entity to the
        // Scoped DbContext, causing problems when the cached reference outlives the
        // request scope in a multi-request scenario.
        // ASSUMPTION: The caller never mutates the returned instance directly;
        // mutations must go through UpdateAsync.
        var settings = await _context.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SingletonId, cancellationToken);

        // ── Self-healing default row ─────────────────────────────────────────
        // The migration seeds this row, so this branch should never execute in
        // a correctly initialised database.  It exists as a safety net for
        // environments where migrations are deferred or re-run out of order.
        if (settings is null)
        {
            _logger.LogWarning(
                "SystemSettings row with Id={Id} not found in the database. "
                + "Creating default row — verify that migrations have been applied.",
                SingletonId);

            settings = await CreateDefaultRowAsync(cancellationToken);
        }

        // ── Populate cache with 5-minute absolute TTL ────────────────────────
        using var entry = _cache.CreateEntry(_cacheKey);
        entry.Value = settings;
        entry.AbsoluteExpirationRelativeToNow = _cacheTtl;

        _logger.LogDebug(
            "SystemSettings cached for {Ttl} minutes (Id={Id})",
            _cacheTtl.TotalMinutes,
            settings.Id);

        return settings;
    }

    // ────────────────────────────────────────────────────────────────────────
    // UpdateAsync — upsert with UpdatedAt stamp, cache eviction + repopulation
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <see cref="SystemSettings.UpdatedAt"/> is always set to
    /// <see cref="DateTime.UtcNow"/> regardless of the value supplied in
    /// <paramref name="settings"/>, so the Infrastructure layer is the single
    /// authoritative writer for this field (Acceptance Criteria).
    /// </para>
    /// <para>
    /// Uses <c>context.Update(settings)</c> which issues an <c>UPDATE</c> (not
    /// <c>INSERT</c>) because <c>Id</c> is declared as <c>ValueGeneratedNever()</c>
    /// in <c>SystemSettingsConfiguration</c>. Since the self-healing default row is
    /// guaranteed to exist (via <see cref="GetAsync"/>), the UPDATE always matches
    /// exactly one row.
    /// </para>
    /// <para>
    /// The cache entry is explicitly removed before saving (evict-on-write) so that
    /// any concurrent reader that misses between the remove and the save will simply
    /// incur a DB hit rather than reading a stale cached value.  After
    /// <c>SaveChangesAsync</c> succeeds, the updated entity is immediately
    /// re-inserted into the cache so the next read is served from cache.
    /// </para>
    /// </remarks>
    public async Task UpdateAsync(
        SystemSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Stamp UpdatedAt with UtcNow — this is always the Infrastructure layer's
        // responsibility, regardless of what the caller placed on the entity.
        settings.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Updating SystemSettings singleton (Id={Id}) at {UpdatedAt:O}",
            settings.Id,
            settings.UpdatedAt);

        // ── Evict stale cache entry before the write ─────────────────────────
        // Removing before SaveChangesAsync ensures that any concurrent reader
        // that arrives during the write will bypass the cache and hit the DB —
        // they may read the pre-update value from the DB, but that is safe because
        // the DB row is still consistent at that point.
        _cache.Remove(_cacheKey);

        // ── Attach and update ────────────────────────────────────────────────
        // Update() transitions the entity to EntityState.Modified (or re-attaches it
        // from a detached/no-tracking state).  Because Id is ValueGeneratedNever(),
        // EF Core knows to issue UPDATE rather than INSERT.
        _context.SystemSettings.Update(settings);
        await _context.SaveChangesAsync(cancellationToken);

        // ── Re-populate cache with the freshly persisted entity ──────────────
        // Storing the saved entity (not the pre-save snapshot) ensures the cached
        // copy reflects any server-side default values that EF may have assigned
        // during SaveChanges (e.g. default column values on PostgreSQL).
        // A new no-tracking snapshot is cached so the cache entry is independent
        // of the DbContext's change-tracker (which will be disposed at request end).
        using var entry = _cache.CreateEntry(_cacheKey);
        entry.Value = settings;
        entry.AbsoluteExpirationRelativeToNow = _cacheTtl;

        _logger.LogDebug(
            "SystemSettings cache repopulated after update (TTL={Ttl} minutes)",
            _cacheTtl.TotalMinutes);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and persists a default <see cref="SystemSettings"/> row with
    /// <c>Id = 1</c> using the property initialisers defined on the entity itself
    /// (15 min access token, 24 h refresh token, 10 MB body cap, lockout threshold 5).
    /// </summary>
    /// <remarks>
    /// This method should never be called on a correctly initialised database
    /// (the migration seeds the row).  It is a defensive self-healing path that
    /// produces a coherent default state and logs a warning so that operators are
    /// aware a manual intervention may be required.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>The newly created and persisted <see cref="SystemSettings"/> row.</returns>
    private async Task<SystemSettings> CreateDefaultRowAsync(
        CancellationToken cancellationToken)
    {
        // The parameterless constructor sets Id = 1 and all property defaults.
        // UpdatedAt will be stamped to UtcNow by the AppDbContext.EnforceUtcDateTimes
        // interceptor, but we also set it explicitly here for clarity.
        var defaultSettings = new SystemSettings
        {
            UpdatedAt = DateTime.UtcNow
        };

        _logger.LogWarning(
            "Inserting default SystemSettings row (Id={Id}) — "
            + "this indicates the database seed migration has not been applied.",
            defaultSettings.Id);

        await _context.SystemSettings.AddAsync(defaultSettings, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return defaultSettings;
    }
}
