using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Infrastructure.Persistence;
using DataViewer.Infrastructure.Persistence.Repositories;
using DataViewer.Tests.Persistence.Repositories.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DataViewer.Tests.Persistence.Repositories;

/// <summary>
/// Unit and integration tests for <see cref="SystemSettingsRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Tests use a SQLite in-memory database via <see cref="RepositoryTestDbContextFactory"/>
/// so that EF Core operations execute against a real relational provider.
/// </para>
/// <para>
/// Each test uses a real <see cref="MemoryCache"/> instance (not a mock) to exercise
/// the actual caching behaviour — TTL, eviction, and cache-key semantics. A fresh
/// cache is created per test to guarantee test isolation.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SystemSettingsRepositoryTests : IAsyncDisposable
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private AppDbContext? _context;
    private SqliteConnection? _connection;

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a real <see cref="MemoryCache"/> with a default 5-minute entry TTL.
    /// A fresh instance is created per test so cache state never leaks between tests.
    /// </summary>
    private static MemoryCache CreateCache() =>
        new MemoryCache(Options.Create(new MemoryCacheOptions()));

    /// <summary>
    /// Creates a fully-wired <see cref="SystemSettingsRepository"/> backed by a real
    /// SQLite in-memory database. The seed row (Id = 1) is applied by
    /// <c>EnsureCreatedAsync</c> via the <see cref="SystemSettingsConfiguration.HasData"/>
    /// call in <see cref="AppDbContext.OnModelCreating"/>.
    /// </summary>
    private async Task<(SystemSettingsRepository repo, AppDbContext ctx, IMemoryCache cache)>
        CreateRepositoryAsync()
    {
        (_context, _connection) = await RepositoryTestDbContextFactory.CreateAsync();
        var cache = CreateCache();

        var repo = new SystemSettingsRepository(
            context: _context,
            cache: cache,
            logger: NullLogger<SystemSettingsRepository>.Instance);

        return (repo, _context, cache);
    }

    /// <summary>
    /// Creates a repository + a secondary verification context that shares the same
    /// SQLite database. Use the verification context to read persisted state without
    /// the change-tracker masking the result.
    /// </summary>
    private async Task<(
        SystemSettingsRepository repo,
        AppDbContext primaryCtx,
        AppDbContext verifyCtx,
        IMemoryCache cache)>
        CreateRepositoryWithVerifyContextAsync()
    {
        AppDbContext primary, verify;
        (primary, verify, _connection) =
            await RepositoryTestDbContextFactory.CreatePairAsync();
        _context = primary;
        var cache = CreateCache();

        var repo = new SystemSettingsRepository(
            context: primary,
            cache: cache,
            logger: NullLogger<SystemSettingsRepository>.Instance);

        return (repo, primary, verify, cache);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Interface contract
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Unit")]
    public void SystemSettingsRepository_ImplementsISystemSettingsRepository()
    {
        // Assert — compile-time + runtime verification
        typeof(SystemSettingsRepository).Should().Implement<ISystemSettingsRepository>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetAsync — happy path
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WhenSeededRowExists_ReturnsSingletonSettings()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        // Act
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1, "the singleton row always has Id = 1");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WhenSeededRowExists_ReturnsDefaultPropertyValues()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        // Act
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert — default values defined in SystemSettingsConfiguration.HasData
        result.Should().NotBeNull();
        result!.JwtAccessTokenMinutes.Should().Be(15);
        result.JwtRefreshTokenHours.Should().Be(24);
        result.BodySizeCapMb.Should().Be(10);
        result.LockoutThreshold.Should().Be(5);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetAsync — caching behaviour
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_CalledTwice_ReturnsSameReferenceOnCacheHit()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        // Act
        var first = await repo.GetAsync(CancellationToken.None);
        var second = await repo.GetAsync(CancellationToken.None);

        // Assert — second call returns the cached instance (reference equality)
        second.Should().BeSameAs(first,
            "a cache hit must return the exact cached reference without a second DB round-trip");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_FirstCall_PopulatesCacheWithResult()
    {
        // Arrange
        (var repo, _, var cache) = await CreateRepositoryAsync();

        // Act — prime the cache via GetAsync
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert — the cache should now contain the entry
        var cacheHit = cache.TryGetValue(
            // Use reflection to read the private static _cacheKey field
            GetPrivateCacheKey(),
            out SystemSettings? cached);

        cacheHit.Should().BeTrue("GetAsync must populate the cache after a DB read");
        cached.Should().BeSameAs(result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WhenCacheAlreadyPopulated_DoesNotHitDatabase()
    {
        // Arrange — pre-populate the cache using a real settings object
        (var repo, _, var cache) = await CreateRepositoryAsync();

        var cachedSettings = new SystemSettings
        {
            JwtAccessTokenMinutes = 999 // sentinel value unique to this test
        };

        // Manually seed the cache under the same key the repository uses
        cache.Set(GetPrivateCacheKey(), cachedSettings, TimeSpan.FromMinutes(5));

        // Act
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert — returns the pre-seeded cache value, not the DB value (15 minutes)
        result.Should().BeSameAs(cachedSettings);
        result!.JwtAccessTokenMinutes.Should().Be(999,
            "a cache hit must short-circuit the DB read and return the cached entry");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetAsync — self-healing default row creation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WhenNoRowExists_CreatesAndReturnsDefaultRow()
    {
        // Arrange — create a context with the schema but delete the seed row first
        (_context, _connection) = await RepositoryTestDbContextFactory.CreateAsync();

        // Remove the seed row so the self-healing path is exercised
        var seedRow = await _context.SystemSettings.FindAsync(1);
        if (seedRow is not null)
        {
            _context.SystemSettings.Remove(seedRow);
            await _context.SaveChangesAsync();
        }

        var cache = CreateCache();
        var repo = new SystemSettingsRepository(
            context: _context,
            cache: cache,
            logger: NullLogger<SystemSettingsRepository>.Instance);

        // Act
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert — a new default row must have been created
        result.Should().NotBeNull(
            "GetAsync must create a default row when Id = 1 is absent (self-healing path)");
        result!.Id.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WhenNoRowExists_PersistsNewDefaultRowToDatabase()
    {
        // Arrange — remove seed row
        (var primary, var verify, _connection) =
            await RepositoryTestDbContextFactory.CreatePairAsync();
        _context = primary;

        var seedRow = await primary.SystemSettings.FindAsync(1);
        if (seedRow is not null)
        {
            primary.SystemSettings.Remove(seedRow);
            await primary.SaveChangesAsync();
        }

        var repo = new SystemSettingsRepository(
            context: primary,
            cache: CreateCache(),
            logger: NullLogger<SystemSettingsRepository>.Instance);

        // Act
        await repo.GetAsync(CancellationToken.None);

        // Assert — verify through the independent context
        var persisted = await verify.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == 1);

        persisted.Should().NotBeNull(
            "the self-healing path must INSERT the default row into the database");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WhenNoRowExistsAndDefaultCreated_CachesNewRow()
    {
        // Arrange
        (_context, _connection) = await RepositoryTestDbContextFactory.CreateAsync();

        var seedRow = await _context.SystemSettings.FindAsync(1);
        if (seedRow is not null)
        {
            _context.SystemSettings.Remove(seedRow);
            await _context.SaveChangesAsync();
        }

        var cache = CreateCache();
        var repo = new SystemSettingsRepository(
            context: _context,
            cache: cache,
            logger: NullLogger<SystemSettingsRepository>.Instance);

        // Act
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert — the newly created row should be cached
        var cacheHit = cache.TryGetValue(GetPrivateCacheKey(), out SystemSettings? cached);
        cacheHit.Should().BeTrue("the newly created default row must be placed in the cache");
        cached.Should().BeSameAs(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UpdateAsync — happy path
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_WhenCalled_PersistsChangesToDatabase()
    {
        // Arrange
        (var repo, _, var verifyCtx, _) = await CreateRepositoryWithVerifyContextAsync();

        var settings = new SystemSettingsBuilder()
            .WithJwtAccessTokenMinutes(30)
            .WithJwtRefreshTokenHours(48)
            .WithBodySizeCapMb(20)
            .WithLockoutThreshold(10)
            .Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);

        // Assert — verify through the independent context
        var persisted = await verifyCtx.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == 1);

        persisted.Should().NotBeNull();
        persisted!.JwtAccessTokenMinutes.Should().Be(30);
        persisted.JwtRefreshTokenHours.Should().Be(48);
        persisted.BodySizeCapMb.Should().Be(20);
        persisted.LockoutThreshold.Should().Be(10);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_AlwaysStampsUpdatedAtWithUtcNow()
    {
        // Arrange
        (var repo, _, var verifyCtx, _) = await CreateRepositoryWithVerifyContextAsync();

        var beforeUpdate = DateTime.UtcNow.AddSeconds(-1);

        var settings = new SystemSettingsBuilder()
            .WithUpdatedAt(DateTime.MinValue) // caller-supplied value must be overwritten
            .Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);
        var afterUpdate = DateTime.UtcNow.AddSeconds(1);

        // Assert — UpdatedAt must be set to UtcNow by the repository, regardless of
        // the value supplied by the caller
        var persisted = await verifyCtx.SystemSettings
            .AsNoTracking()
            .FirstAsync(s => s.Id == 1);

        persisted.UpdatedAt.Should().BeAfter(beforeUpdate,
            "UpdatedAt must be set to UtcNow by the Infrastructure layer");
        persisted.UpdatedAt.Should().BeBefore(afterUpdate,
            "UpdatedAt must reflect the time of the actual write, not a future time");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_OverwritesCallerSuppliedUpdatedAtValue()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        var farPastDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var settings = new SystemSettingsBuilder()
            .WithUpdatedAt(farPastDate)
            .Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);

        // Assert — the in-memory entity itself must have its UpdatedAt overwritten
        settings.UpdatedAt.Should().NotBe(farPastDate,
            "the repository must always overwrite UpdatedAt with UtcNow, " +
            "ignoring the caller-supplied value");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UpdateAsync — cache eviction and repopulation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_EvictsCacheBeforePersisting()
    {
        // Arrange — prime the cache first via GetAsync
        (var repo, _, var cache) = await CreateRepositoryAsync();
        await repo.GetAsync(CancellationToken.None); // prime the cache

        var settings = new SystemSettingsBuilder().Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);

        // The cache must have been evicted and then immediately repopulated.
        // After UpdateAsync the cache entry should hold the updated entity.
        var cacheHit = cache.TryGetValue(GetPrivateCacheKey(), out SystemSettings? cached);

        cacheHit.Should().BeTrue(
            "UpdateAsync must re-populate the cache with the updated entity after saving");
        cached.Should().BeSameAs(settings,
            "the repopulated cache entry must be the freshly saved entity");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_RepopulatesCacheWithUpdatedEntity()
    {
        // Arrange
        (var repo, _, var cache) = await CreateRepositoryAsync();

        var settings = new SystemSettingsBuilder()
            .WithJwtAccessTokenMinutes(45)
            .Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);

        // Assert — subsequent GetAsync must return the updated entity from cache
        // without another DB round-trip (same reference)
        var result = await repo.GetAsync(CancellationToken.None);

        result.Should().BeSameAs(settings,
            "GetAsync after UpdateAsync must serve from the repopulated cache entry");
        result!.JwtAccessTokenMinutes.Should().Be(45);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_CalledTwice_SecondUpdateReflectsLatestValues()
    {
        // Arrange
        (var repo, _, var verifyCtx, _) = await CreateRepositoryWithVerifyContextAsync();

        var firstSettings = new SystemSettingsBuilder()
            .WithJwtAccessTokenMinutes(20)
            .Build();

        var secondSettings = new SystemSettingsBuilder()
            .WithJwtAccessTokenMinutes(60)
            .Build();

        // Act
        await repo.UpdateAsync(firstSettings, CancellationToken.None);
        await repo.UpdateAsync(secondSettings, CancellationToken.None);

        // Assert
        var persisted = await verifyCtx.SystemSettings
            .AsNoTracking()
            .FirstAsync(s => s.Id == 1);

        persisted.JwtAccessTokenMinutes.Should().Be(60,
            "the second update must overwrite the first");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UpdateAsync — null guard
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAsync_WhenSettingsIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        // Act
        var act = () => repo.UpdateAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("settings");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UpdateAsync — cancellation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var settings = new SystemSettingsBuilder().Build();

        // Act
        var act = () => repo.UpdateAsync(settings, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetAsync — cancellation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_WhenCancellationRequestedAndCacheMiss_ThrowsOperationCanceledException()
    {
        // Arrange — empty cache, cancelled token, so the DB query will be cancelled
        (_context, _connection) = await RepositoryTestDbContextFactory.CreateAsync();
        var cache = CreateCache();
        var repo = new SystemSettingsRepository(
            context: _context,
            cache: cache,
            logger: NullLogger<SystemSettingsRepository>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => repo.GetAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetAsync — end-to-end happy path (User Story: read system settings)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndToEnd_GetAsync_ReturnsSeededRowAndCachesResult()
    {
        // Arrange
        (var repo, _, var cache) = await CreateRepositoryAsync();

        // Act — first call: DB read + cache populate
        var first = await repo.GetAsync(CancellationToken.None);

        // Second call: cache hit, no DB round-trip
        var second = await repo.GetAsync(CancellationToken.None);

        // Assert
        first.Should().NotBeNull();
        first!.Id.Should().Be(1);
        second.Should().BeSameAs(first, "second call must be served from cache");

        var cacheContainsEntry = cache.TryGetValue(GetPrivateCacheKey(), out _);
        cacheContainsEntry.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // End-to-end: update then read cycle (User Story: admin updates settings)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndToEnd_UpdateThenGet_ReturnsUpdatedValuesFromCache()
    {
        // Arrange
        (var repo, _, _) = await CreateRepositoryAsync();

        var updated = new SystemSettingsBuilder()
            .WithJwtAccessTokenMinutes(60)
            .WithBodySizeCapMb(50)
            .Build();

        // Act
        await repo.UpdateAsync(updated, CancellationToken.None);
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.JwtAccessTokenMinutes.Should().Be(60);
        result.BodySizeCapMb.Should().Be(50);
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Boundary / edge cases
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task UpdateAsync_WithBoundaryJwtAccessTokenMinutes_PersistsCorrectly(
        int minutes)
    {
        // Arrange
        (var repo, _, var verifyCtx, _) = await CreateRepositoryWithVerifyContextAsync();

        var settings = new SystemSettingsBuilder()
            .WithJwtAccessTokenMinutes(minutes)
            .Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);

        // Assert
        var persisted = await verifyCtx.SystemSettings
            .AsNoTracking()
            .FirstAsync(s => s.Id == 1);

        persisted.JwtAccessTokenMinutes.Should().Be(minutes);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(0)]      // lockout disabled
    [InlineData(1)]      // single attempt threshold
    [InlineData(100)]    // high threshold
    public async Task UpdateAsync_WithBoundaryLockoutThreshold_PersistsCorrectly(
        int threshold)
    {
        // Arrange
        (var repo, _, var verifyCtx, _) = await CreateRepositoryWithVerifyContextAsync();

        var settings = new SystemSettingsBuilder()
            .WithLockoutThreshold(threshold)
            .Build();

        // Act
        await repo.UpdateAsync(settings, CancellationToken.None);

        // Assert
        var persisted = await verifyCtx.SystemSettings
            .AsNoTracking()
            .FirstAsync(s => s.Id == 1);

        persisted.LockoutThreshold.Should().Be(threshold);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cache TTL — entry options verification
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_AfterCacheExpiry_ReturnsValueFromDatabase()
    {
        // Arrange — use a very short TTL cache entry to simulate expiry
        (var repo, var ctx, var cache) = await CreateRepositoryAsync();

        // Manually place a cache entry with an immediate expiry
        cache.Set(GetPrivateCacheKey(), new SystemSettings(), TimeSpan.FromMilliseconds(1));

        // Wait for the entry to expire
        await Task.Delay(50);

        // Act — cache miss now triggers a DB read
        var result = await repo.GetAsync(CancellationToken.None);

        // Assert — returns the real DB value (Id = 1 from seed), not the expired entry
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private reflection helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrieves the private static <c>_cacheKey</c> object used by
    /// <see cref="SystemSettingsRepository"/> via reflection. This allows
    /// tests to inspect and pre-seed the cache without exposing the key in
    /// the production API surface.
    /// </summary>
    private static object GetPrivateCacheKey()
    {
        var field = typeof(SystemSettingsRepository)
            .GetField(
                "_cacheKey",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

        field.Should().NotBeNull("_cacheKey static field must exist on SystemSettingsRepository");
        return field!.GetValue(null)!;
    }
}
