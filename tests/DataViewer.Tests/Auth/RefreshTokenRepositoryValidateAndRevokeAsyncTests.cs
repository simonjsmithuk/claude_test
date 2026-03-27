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
/// Integration tests for <see cref="RefreshTokenRepository.ValidateAndRevokeAsync"/>.
/// Verifies the atomic validate-and-revoke pattern: valid tokens are revoked and
/// returned; invalid/expired/already-revoked tokens yield null.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RefreshTokenRepositoryValidateAndRevokeAsyncTests : IAsyncDisposable
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

        // Seed a user required by the foreign key
        var user = UserBuilder.AViewer().Build();
        _primaryContext.Users.Add(user);
        await _primaryContext.SaveChangesAsync();
        _seedUserId = user.Id;

        return new RefreshTokenRepository(
            _primaryContext,
            NullLogger<RefreshTokenRepository>.Instance);
    }

    /// <summary>
    /// Seeds a <see cref="RefreshToken"/> row directly into the DB and returns its hash.
    /// </summary>
    private async Task<string> SeedActiveTokenAsync(
        AppDbContext context,
        Guid userId,
        string hash,
        DateTime? expiresAt = null,
        bool isRevoked = false)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            IsRevoked = isRevoked,
            CreatedAt = DateTime.UtcNow,
        };
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();
        return hash;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Happy-path tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateAndRevokeAsync_WithValidNonExpiredToken_ReturnsRefreshTokenEntity()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash);

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        result.Should().NotBeNull(
            because: "a valid, non-revoked, non-expired token must be returned");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithValidToken_SetsIsRevokedToTrue()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash);

        // Act
        await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert — verify via independent context
        var persisted = await _verifyContext!.RefreshTokens
            .AsNoTracking()
            .FirstAsync(rt => rt.TokenHash == hash);

        persisted.IsRevoked.Should().BeTrue(
            because: "validate-and-revoke must atomically mark the token as revoked");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithValidToken_SetsRevokedAtToApproximatelyNow()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash);
        var before = DateTime.UtcNow;

        // Act
        await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);
        var after = DateTime.UtcNow;

        // Assert
        var persisted = await _verifyContext!.RefreshTokens
            .AsNoTracking()
            .FirstAsync(rt => rt.TokenHash == hash);

        persisted.RevokedAt.Should().NotBeNull(because: "RevokedAt must be set on revocation");
        persisted.RevokedAt!.Value.Should()
            .BeOnOrAfter(before.AddSeconds(-1))
            .And.BeOnOrBefore(after.AddSeconds(1),
                because: "RevokedAt must reflect the time of the revocation call");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithValidToken_ReturnsEntityWithUserNavigationProperty()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash);

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert — User navigation property must be populated so callers can issue new tokens
        result!.User.Should().NotBeNull(
            because: "the returned entity must include the User navigation property for immediate token issuance");
        result.User.Id.Should().Be(_seedUserId,
            because: "the User navigation property must reference the correct owner");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithValidToken_ReturnedEntityHasCorrectTokenHash()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash);

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        result!.TokenHash.Should().Be(hash);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Invalid / already-revoked / expired token tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateAndRevokeAsync_WithUnknownHash_ReturnsNull()
    {
        // Arrange
        var repo = await SetupAsync();
        const string nonExistentHash = "0000000000000000000000000000000000000000000000000000000000000000";

        // Act
        var result = await repo.ValidateAndRevokeAsync(nonExistentHash, CancellationToken.None);

        // Assert
        result.Should().BeNull(
            because: "an unknown token hash must return null");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithAlreadyRevokedToken_ReturnsNull()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "ffff6666ffff6666ffff6666ffff6666ffff6666ffff6666ffff6666ffff6666";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash, isRevoked: true);

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        result.Should().BeNull(
            because: "an already-revoked token must be rejected and null returned");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithExpiredToken_ReturnsNull()
    {
        // Arrange
        var repo = await SetupAsync();
        const string hash = "7777aaaa7777aaaa7777aaaa7777aaaa7777aaaa7777aaaa7777aaaa7777aaaa";
        // ExpiresAt in the past
        await SeedActiveTokenAsync(
            _primaryContext!, _seedUserId, hash,
            expiresAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        result.Should().BeNull(
            because: "an expired token (ExpiresAt < UtcNow) must be rejected");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WithExpiredAndRevokedToken_ReturnsNull()
    {
        // Arrange — both conditions: expired AND already revoked
        var repo = await SetupAsync();
        const string hash = "8888bbbb8888bbbb8888bbbb8888bbbb8888bbbb8888bbbb8888bbbb8888bbbb";
        await SeedActiveTokenAsync(
            _primaryContext!, _seedUserId, hash,
            expiresAt: DateTime.UtcNow.AddDays(-1),
            isRevoked: true);

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        result.Should().BeNull(
            because: "a token that is both expired and revoked must not be returned");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Atomicity / TOCTOU race condition safety
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateAndRevokeAsync_CalledTwiceWithSameHash_SecondCallReturnsNull()
    {
        // Arrange — simulates a token-rotation replay attack
        var repo = await SetupAsync();
        const string hash = "9999cccc9999cccc9999cccc9999cccc9999cccc9999cccc9999cccc9999cccc";
        await SeedActiveTokenAsync(_primaryContext!, _seedUserId, hash);

        // Act
        var firstResult = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);
        var secondResult = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        firstResult.Should().NotBeNull(because: "the first call must succeed");
        secondResult.Should().BeNull(
            because: "a token already revoked by the first call must be rejected on the second call — " +
                     "this proves the atomic validate-and-revoke prevents replay attacks");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Input guard tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateAndRevokeAsync_WhenTokenHashIsNull_ThrowsArgumentException()
    {
        // Arrange
        var repo = await SetupAsync();

        // Act
        var act = () => repo.ValidateAndRevokeAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tokenHash");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WhenTokenHashIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var repo = await SetupAsync();

        // Act
        var act = () => repo.ValidateAndRevokeAsync(string.Empty, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tokenHash");
    }

    [Fact]
    public async Task ValidateAndRevokeAsync_WhenTokenHashIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var repo = await SetupAsync();

        // Act
        var act = () => repo.ValidateAndRevokeAsync("   ", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tokenHash");
    }
}
