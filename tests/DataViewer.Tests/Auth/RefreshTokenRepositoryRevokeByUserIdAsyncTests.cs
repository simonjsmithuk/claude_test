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
/// Integration tests for <see cref="RefreshTokenRepository.RevokeByUserIdAsync"/>.
/// Verifies the bulk-revocation (logout-all-sessions) path:
/// - All active tokens for the user are revoked atomically.
/// - Already-revoked and expired tokens are left unchanged.
/// - The return value equals the number of rows actually revoked.
/// - The operation is idempotent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RefreshTokenRepositoryRevokeByUserIdAsyncTests : IAsyncDisposable
{
    private AppDbContext? _primaryContext;
    private AppDbContext? _verifyContext;
    private SqliteConnection? _connection;
    private Guid _seedUserId;

    public async ValueTask DisposeAsync()
    {
        if (_primaryContext is not null) await _primaryContext.DisposeAsync();
        if (_verifyContext is not null) await _verifyContext.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }

    // ── Setup helpers ─────────────────────────────────────────────────────────

    private async Task<RefreshTokenRepository> SetupAsync()
    {
        (_primaryContext, _verifyContext, _connection) =
            await RefreshTokenTestDbContextFactory.CreatePairAsync();

        var user = UserBuilder.AViewer().Build();
        _primaryContext.Users.Add(user);
        await _primaryContext.SaveChangesAsync();
        _seedUserId = user.Id;

        return new RefreshTokenRepository(
            _primaryContext,
            NullLogger<RefreshTokenRepository>.Instance);
    }

    private async Task SeedTokenAsync(
        string hash,
        Guid userId,
        bool isRevoked = false,
        DateTime? expiresAt = null)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            IsRevoked = isRevoked,
            CreatedAt = DateTime.UtcNow,
        };
        _primaryContext!.RefreshTokens.Add(token);
        await _primaryContext.SaveChangesAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Happy-path: returns correct count
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeByUserIdAsync_WithSingleActiveToken_ReturnsOne()
    {
        // Arrange
        var repo = await SetupAsync();
        await SeedTokenAsync("aaaa0001aaaa0001aaaa0001aaaa0001aaaa0001aaaa0001aaaa0001aaaa0001", _seedUserId);

        // Act
        var revoked = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert
        revoked.Should().Be(1,
            because: "there is exactly one active token; one row must be revoked");
    }

    [Fact]
    public async Task RevokeByUserIdAsync_WithMultipleActiveTokens_ReturnsCorrectCount()
    {
        // Arrange
        var repo = await SetupAsync();
        await SeedTokenAsync("bbbb0001bbbb0001bbbb0001bbbb0001bbbb0001bbbb0001bbbb0001bbbb0001", _seedUserId);
        await SeedTokenAsync("bbbb0002bbbb0002bbbb0002bbbb0002bbbb0002bbbb0002bbbb0002bbbb0002", _seedUserId);
        await SeedTokenAsync("bbbb0003bbbb0003bbbb0003bbbb0003bbbb0003bbbb0003bbbb0003bbbb0003", _seedUserId);

        // Act
        var revoked = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert
        revoked.Should().Be(3,
            because: "all 3 active tokens must be revoked in a single bulk operation");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Persistence verification
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeByUserIdAsync_WithActiveTokens_MarksAllAsRevoked()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash1 = "cccc0001cccc0001cccc0001cccc0001cccc0001cccc0001cccc0001cccc0001";
        const string hash2 = "cccc0002cccc0002cccc0002cccc0002cccc0002cccc0002cccc0002cccc0002";
        await SeedTokenAsync(hash1, _seedUserId);
        await SeedTokenAsync(hash2, _seedUserId);

        // Act
        await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert — verify both tokens are now revoked
        var tokens = await _verifyContext!.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.UserId == _seedUserId)
            .ToListAsync();

        tokens.Should().AllSatisfy(t =>
            t.IsRevoked.Should().BeTrue(
                because: "every active token for the user must be revoked on logout-all"));
    }

    [Fact]
    public async Task RevokeByUserIdAsync_WithActiveTokens_SetsRevokedAtForAllRevokedRows()
    {
        // Arrange
        var repo = await SetupAsync();
        var before = DateTime.UtcNow;
        await SeedTokenAsync("dddd0001dddd0001dddd0001dddd0001dddd0001dddd0001dddd0001dddd0001", _seedUserId);
        await SeedTokenAsync("dddd0002dddd0002dddd0002dddd0002dddd0002dddd0002dddd0002dddd0002", _seedUserId);

        // Act
        await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);
        var after = DateTime.UtcNow;

        // Assert
        var tokens = await _verifyContext!.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.UserId == _seedUserId)
            .ToListAsync();

        tokens.Should().AllSatisfy(t =>
        {
            t.RevokedAt.Should().NotBeNull();
            t.RevokedAt!.Value.Should()
                .BeOnOrAfter(before.AddSeconds(-1))
                .And.BeOnOrBefore(after.AddSeconds(1));
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Already-revoked tokens are left unchanged
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeByUserIdAsync_WhenSomeTokensAlreadyRevoked_OnlyRevokesActiveOnes()
    {
        // Arrange
        var repo = await SetupAsync();
        const string activeHash = "eeee0001eeee0001eeee0001eeee0001eeee0001eeee0001eeee0001eeee0001";
        const string revokedHash = "eeee0002eeee0002eeee0002eeee0002eeee0002eeee0002eeee0002eeee0002";
        await SeedTokenAsync(activeHash, _seedUserId, isRevoked: false);
        await SeedTokenAsync(revokedHash, _seedUserId, isRevoked: true);

        // Act
        var revoked = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert
        revoked.Should().Be(1,
            because: "only the one active token must be revoked; already-revoked tokens are skipped");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Expired tokens are left unchanged
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeByUserIdAsync_WhenSomeTokensExpired_OnlyRevokesNonExpiredActiveOnes()
    {
        // Arrange
        var repo = await SetupAsync();
        const string activeHash = "ffff0001ffff0001ffff0001ffff0001ffff0001ffff0001ffff0001ffff0001";
        const string expiredHash = "ffff0002ffff0002ffff0002ffff0002ffff0002ffff0002ffff0002ffff0002";
        await SeedTokenAsync(activeHash, _seedUserId, expiresAt: DateTime.UtcNow.AddDays(7));
        await SeedTokenAsync(expiredHash, _seedUserId, expiresAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var revoked = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert
        revoked.Should().Be(1,
            because: "expired tokens must not be counted; only the active, non-expired token is revoked");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Idempotency
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeByUserIdAsync_WhenNoActiveTokensExist_ReturnsZeroWithoutException()
    {
        // Arrange
        var repo = await SetupAsync();
        // Do not seed any tokens

        // Act
        var act = () => repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);
        var revoked = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync(
            because: "RevokeByUserIdAsync is idempotent; no tokens → no error");
        revoked.Should().Be(0,
            because: "zero active tokens must result in zero revoked rows");
    }

    [Fact]
    public async Task RevokeByUserIdAsync_CalledTwice_SecondCallRevokesZeroAdditionalTokens()
    {
        // Arrange
        var repo = await SetupAsync();
        await SeedTokenAsync("aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111", _seedUserId);

        // Act
        var firstCall = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);
        var secondCall = await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert
        firstCall.Should().Be(1, because: "the first call revokes the one active token");
        secondCall.Should().Be(0,
            because: "the second call finds no active tokens and revokes nothing (idempotent)");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cross-user isolation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeByUserIdAsync_WithTokensForMultipleUsers_OnlyRevokesTargetUsersTokens()
    {
        // Arrange
        var repo = await SetupAsync();

        // Seed a second user
        var otherUser = UserBuilder.AViewer().Build();
        _primaryContext!.Users.Add(otherUser);
        await _primaryContext.SaveChangesAsync();
        var otherUserId = otherUser.Id;

        const string targetUserHash = "bbbb1111bbbb1111bbbb1111bbbb1111bbbb1111bbbb1111bbbb1111bbbb1111";
        const string otherUserHash = "bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222";
        await SeedTokenAsync(targetUserHash, _seedUserId);
        await SeedTokenAsync(otherUserHash, otherUserId);

        // Act — revoke only the target user's tokens
        await repo.RevokeByUserIdAsync(_seedUserId, CancellationToken.None);

        // Assert — the other user's token must not be affected
        var otherUserToken = await _verifyContext!.RefreshTokens
            .AsNoTracking()
            .FirstAsync(rt => rt.TokenHash == otherUserHash);

        otherUserToken.IsRevoked.Should().BeFalse(
            because: "RevokeByUserIdAsync must not affect tokens belonging to other users");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // IRefreshTokenRepository interface contract
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTokenRepository_ImplementsIRefreshTokenRepositoryInterface()
    {
        typeof(RefreshTokenRepository).Should()
            .Implement<DataViewer.Application.Interfaces.IRefreshTokenRepository>(
                because: "RefreshTokenRepository must implement the Application-layer IRefreshTokenRepository contract");
    }
}
