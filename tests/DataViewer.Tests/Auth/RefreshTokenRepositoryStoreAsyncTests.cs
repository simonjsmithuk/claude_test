using DataViewer.Domain.Entities;
using DataViewer.Infrastructure.Auth;
using DataViewer.Infrastructure.Persistence;
using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Integration tests for <see cref="RefreshTokenRepository.StoreAsync"/>.
/// Uses a real SQLite in-memory database to verify that:
/// - the token hash (never the raw token) is persisted,
/// - the entity is retrievable after saving,
/// - expiry, userId, and IsRevoked=false invariants are satisfied.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RefreshTokenRepositoryStoreAsyncTests : IAsyncDisposable
{
    private AppDbContext? _context;
    private AppDbContext? _verifyContext;
    private SqliteConnection? _connection;

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        if (_verifyContext is not null) await _verifyContext.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(RefreshTokenRepository repo, AppDbContext verify)> CreateAsync()
    {
        (_context, _verifyContext, _connection) =
            await RefreshTokenTestDbContextFactory.CreatePairAsync();

        // Seed a user that foreign key constraints require
        var user = UserBuilder.AViewer().Build();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        // Keep userId available via the user entity
        _seedUserId = user.Id;

        var repo = new RefreshTokenRepository(
            _context,
            NullLogger<RefreshTokenRepository>.Instance);

        return (repo, _verifyContext);
    }

    private Guid _seedUserId;

    private async Task SeedUserAsync(AppDbContext context, Guid userId)
    {
        // Only seed if the user doesn't already exist
        var exists = await context.Users.AnyAsync(u => u.Id == userId);
        if (!exists)
        {
            var user = UserBuilder.AViewer().WithId(userId).Build();
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Happy-path tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StoreAsync_WithValidArguments_PersistsRefreshTokenToDatabase()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        var tokenHash = "a".PadRight(64, 'b'); // 64-char lowercase hex-like string
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored = await repo.StoreAsync(_seedUserId, tokenHash, expiresAt, CancellationToken.None);

        // Assert — verify via independent context to bypass change-tracker
        var persisted = await verify.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.Id == stored.Id);

        persisted.Should().NotBeNull(
            because: "StoreAsync must commit the entity to the database");
    }

    [Fact]
    public async Task StoreAsync_WithValidArguments_PersiststokenHashNotRawToken()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        const string fakeHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored = await repo.StoreAsync(_seedUserId, fakeHash, expiresAt, CancellationToken.None);

        // Assert — the persisted hash must equal the supplied hash
        var persisted = await verify.RefreshTokens
            .AsNoTracking()
            .FirstAsync(rt => rt.Id == stored.Id);

        persisted.TokenHash.Should().Be(fakeHash,
            because: "only the hash is stored, never the raw token");
    }

    [Fact]
    public async Task StoreAsync_WithValidArguments_SetsIsRevokedToFalse()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        const string hash = "cafecafecafecafecafecafecafecafecafecafecafecafecafecafecafecafe";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored = await repo.StoreAsync(_seedUserId, hash, expiresAt, CancellationToken.None);

        // Assert — a freshly stored token must not be revoked
        var persisted = await verify.RefreshTokens.AsNoTracking()
            .FirstAsync(rt => rt.Id == stored.Id);

        persisted.IsRevoked.Should().BeFalse(
            because: "a newly stored refresh token is active (not revoked)");
    }

    [Fact]
    public async Task StoreAsync_WithValidArguments_SetsCorrectUserId()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        const string hash = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored = await repo.StoreAsync(_seedUserId, hash, expiresAt, CancellationToken.None);

        // Assert
        var persisted = await verify.RefreshTokens.AsNoTracking()
            .FirstAsync(rt => rt.Id == stored.Id);

        persisted.UserId.Should().Be(_seedUserId,
            because: "the token must be associated with the correct user");
    }

    [Fact]
    public async Task StoreAsync_WithValidArguments_SetsCorrectExpiresAt()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        const string hash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored = await repo.StoreAsync(_seedUserId, hash, expiresAt, CancellationToken.None);

        // Assert
        var persisted = await verify.RefreshTokens.AsNoTracking()
            .FirstAsync(rt => rt.Id == stored.Id);

        persisted.ExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(5),
            because: "the expiry timestamp must be stored accurately");
    }

    [Fact]
    public async Task StoreAsync_WithValidArguments_SetsCreatedAtToApproximatelyNow()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        const string hash = "0000000000000000000000000000000000000000000000000000000000000001";
        var expiresAt = DateTime.UtcNow.AddDays(7);
        var before = DateTime.UtcNow;

        // Act
        var stored = await repo.StoreAsync(_seedUserId, hash, expiresAt, CancellationToken.None);
        var after = DateTime.UtcNow;

        // Assert
        var persisted = await verify.RefreshTokens.AsNoTracking()
            .FirstAsync(rt => rt.Id == stored.Id);

        persisted.CreatedAt.Should().BeOnOrAfter(before.AddSeconds(-1))
            .And.BeOnOrBefore(after.AddSeconds(1),
                because: "CreatedAt must be stamped with the current UTC time at store time");
    }

    [Fact]
    public async Task StoreAsync_WithValidArguments_ReturnsEntityWithGeneratedId()
    {
        // Arrange
        (var repo, _) = await CreateAsync();
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored = await repo.StoreAsync(_seedUserId, hash, expiresAt, CancellationToken.None);

        // Assert
        stored.Id.Should().NotBe(Guid.Empty,
            because: "EF Core must generate a non-empty GUID primary key");
    }

    [Fact]
    public async Task StoreAsync_StoringTwoTokensForSameUser_BothArePersistedIndependently()
    {
        // Arrange
        (var repo, var verify) = await CreateAsync();
        const string hash1 = "1111111111111111111111111111111111111111111111111111111111111111";
        const string hash2 = "2222222222222222222222222222222222222222222222222222222222222222";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        // Act
        var stored1 = await repo.StoreAsync(_seedUserId, hash1, expiresAt, CancellationToken.None);
        var stored2 = await repo.StoreAsync(_seedUserId, hash2, expiresAt, CancellationToken.None);

        // Assert
        var count = await verify.RefreshTokens
            .AsNoTracking()
            .CountAsync(rt => rt.UserId == _seedUserId);

        count.Should().Be(2,
            because: "a user may hold multiple active refresh tokens for concurrent sessions");

        stored1.Id.Should().NotBe(stored2.Id);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Input guard tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StoreAsync_WhenTokenHashIsNull_ThrowsArgumentException()
    {
        // Arrange
        (var repo, _) = await CreateAsync();

        // Act
        var act = () => repo.StoreAsync(
            _seedUserId, null!, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tokenHash");
    }

    [Fact]
    public async Task StoreAsync_WhenTokenHashIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        (var repo, _) = await CreateAsync();

        // Act
        var act = () => repo.StoreAsync(
            _seedUserId, string.Empty, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tokenHash");
    }

    [Fact]
    public async Task StoreAsync_WhenTokenHashIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        (var repo, _) = await CreateAsync();

        // Act
        var act = () => repo.StoreAsync(
            _seedUserId, "   ", DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tokenHash");
    }
}
