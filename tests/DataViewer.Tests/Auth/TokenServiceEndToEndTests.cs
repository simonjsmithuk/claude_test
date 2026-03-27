using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
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
/// End-to-end tests covering the full token lifecycle across <see cref="TokenService"/>
/// and <see cref="RefreshTokenRepository"/> — modelling the workflows described in
/// the TASK-014 acceptance criteria and Product Spec § FR-01 / § FR-03.
/// </summary>
/// <remarks>
/// Each test represents a complete user story scenario:
/// <list type="bullet">
///   <item>US-E2E-01: Successful login → access token + refresh token issued, hash stored.</item>
///   <item>US-E2E-02: Token refresh → old token revoked, new access + refresh token issued.</item>
///   <item>US-E2E-03: Logout all sessions → all refresh tokens revoked.</item>
///   <item>US-E2E-04: Tampered refresh token → ValidateAndRevoke returns null (no escalation).</item>
///   <item>US-E2E-05: Expired refresh token → ValidateAndRevoke returns null.</item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class TokenServiceEndToEndTests : IAsyncDisposable
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly TokenService _tokenService;
    private readonly IDisposable _envCleanup;
    private AppDbContext? _dbContext;
    private SqliteConnection? _connection;

    public TokenServiceEndToEndTests()
    {
        (_tokenService, _envCleanup) = TokenServiceFactory.Create();
    }

    public async ValueTask DisposeAsync()
    {
        _envCleanup.Dispose();
        if (_dbContext is not null) await _dbContext.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<RefreshTokenRepository> CreateRepoAsync()
    {
        (_dbContext, _connection) = await RefreshTokenTestDbContextFactory.CreateAsync();
        return new RefreshTokenRepository(
            _dbContext,
            NullLogger<RefreshTokenRepository>.Instance);
    }

    private async Task SeedUserAsync(User user)
    {
        _dbContext!.Users.Add(user);
        await _dbContext.SaveChangesAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-01: Login — access token + refresh token issuance
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Happy path: a user successfully authenticates.
    /// The system generates an access token and a refresh token;
    /// only the hash of the refresh token is persisted.
    /// </summary>
    [Fact]
    public async Task E2E_Login_AccessTokenAndRefreshTokenAreIssuedAndHashIsStored()
    {
        // Arrange
        var repo = await CreateRepoAsync();
        var user = UserBuilder.AViewer().Build();
        await SeedUserAsync(user);

        // Act — simulate login:
        //   1. Generate access token (returned to client)
        var accessToken = _tokenService.GenerateAccessToken(user);

        //   2. Generate raw refresh token (returned to client)
        var rawRefreshToken = _tokenService.GenerateRefreshToken();

        //   3. Hash the refresh token and store only the hash
        var tokenHash = _tokenService.GetRefreshTokenHash(rawRefreshToken);
        var expiresAt = DateTime.UtcNow.AddDays(7);
        var storedEntity = await repo.StoreAsync(user.Id, tokenHash, expiresAt, CancellationToken.None);

        // Assert — access token is a valid JWT for the user
        var extractedUserId = _tokenService.GetUserIdFromToken(accessToken);
        extractedUserId.Should().Be(user.Id,
            because: "the access token must encode the correct user ID");

        // Assert — only the hash was stored, not the raw token
        storedEntity.TokenHash.Should().Be(tokenHash);
        storedEntity.TokenHash.Should().NotBe(rawRefreshToken,
            because: "the raw token must never be stored in the database");

        // Assert — token is initially not revoked
        storedEntity.IsRevoked.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-02: Token refresh — rotate refresh token
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Happy path: client presents a valid refresh token to obtain a new access token.
    /// The old refresh token is atomically revoked; a new refresh token is issued.
    /// </summary>
    [Fact]
    public async Task E2E_TokenRefresh_OldRefreshTokenIsRevokedAndNewTokensAreIssued()
    {
        // Arrange — simulate login first
        var repo = await CreateRepoAsync();
        var user = UserBuilder.AViewer().Build();
        await SeedUserAsync(user);

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var tokenHash = _tokenService.GetRefreshTokenHash(rawRefreshToken);
        await repo.StoreAsync(user.Id, tokenHash, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Act — simulate token refresh:
        //   The client hashes the raw refresh token and presents the hash for validation.
        var hashFromClient = _tokenService.GetRefreshTokenHash(rawRefreshToken);
        var revokedToken = await repo.ValidateAndRevokeAsync(hashFromClient, CancellationToken.None);

        // Assert — old token was revoked successfully
        revokedToken.Should().NotBeNull(
            because: "a valid refresh token must be revoked and returned on the refresh call");
        revokedToken!.IsRevoked.Should().BeTrue();
        revokedToken.User.Should().NotBeNull(
            because: "User navigation property must be populated for immediate token issuance");

        // Act — issue new tokens for the next session
        var newAccessToken = _tokenService.GenerateAccessToken(revokedToken.User);
        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newTokenHash = _tokenService.GetRefreshTokenHash(newRawRefreshToken);
        var newStoredToken = await repo.StoreAsync(
            revokedToken.UserId, newTokenHash,
            DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Assert — new access token is valid for the same user
        _tokenService.GetUserIdFromToken(newAccessToken).Should().Be(user.Id);

        // Assert — new refresh token hash differs from the old one
        newTokenHash.Should().NotBe(tokenHash,
            because: "token rotation must produce a new, distinct refresh token");

        // Assert — new stored token is not revoked
        newStoredToken.IsRevoked.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-03: Logout all sessions
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Happy path: user explicitly logs out all active sessions.
    /// All refresh tokens for the user are revoked in a single operation.
    /// </summary>
    [Fact]
    public async Task E2E_LogoutAllSessions_AllRefreshTokensAreRevoked()
    {
        // Arrange — simulate the user having 3 concurrent sessions
        var repo = await CreateRepoAsync();
        var user = UserBuilder.AnAdmin().Build();
        await SeedUserAsync(user);

        for (var i = 0; i < 3; i++)
        {
            var raw = _tokenService.GenerateRefreshToken();
            var hash = _tokenService.GetRefreshTokenHash(raw);
            await repo.StoreAsync(user.Id, hash, DateTime.UtcNow.AddDays(7), CancellationToken.None);
        }

        // Act — logout all sessions
        var revokedCount = await repo.RevokeByUserIdAsync(user.Id, CancellationToken.None);

        // Assert
        revokedCount.Should().Be(3,
            because: "all 3 active sessions must be revoked on logout-all");

        // Assert — any subsequent refresh attempt with any of the old tokens must fail
        var allTokens = await _dbContext!.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.UserId == user.Id)
            .ToListAsync();

        allTokens.Should().AllSatisfy(t =>
            t.IsRevoked.Should().BeTrue(),
            because: "every token must be revoked after logout-all");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-04: Tampered refresh token — security boundary
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Security scenario: an attacker presents a refresh token that does not
    /// match any stored hash. The system returns null — no escalation, no leak.
    /// </summary>
    [Fact]
    public async Task E2E_TamperedRefreshToken_ValidateAndRevokeReturnsNull()
    {
        // Arrange — store a legitimate token
        var repo = await CreateRepoAsync();
        var user = UserBuilder.AViewer().Build();
        await SeedUserAsync(user);

        var legitimateRaw = _tokenService.GenerateRefreshToken();
        var legitimateHash = _tokenService.GetRefreshTokenHash(legitimateRaw);
        await repo.StoreAsync(user.Id, legitimateHash, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Attacker fabricates a different raw token and hashes it
        var attackerRaw = _tokenService.GenerateRefreshToken();
        var attackerHash = _tokenService.GetRefreshTokenHash(attackerRaw);

        // Act
        var result = await repo.ValidateAndRevokeAsync(attackerHash, CancellationToken.None);

        // Assert
        result.Should().BeNull(
            because: "a fabricated/tampered refresh token must not validate");

        // Assert — the legitimate token must remain untouched
        var legitimateToken = await _dbContext!.RefreshTokens
            .AsNoTracking()
            .FirstAsync(rt => rt.TokenHash == legitimateHash);

        legitimateToken.IsRevoked.Should().BeFalse(
            because: "the legitimate token must not be revoked by an attacker's failed attempt");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-05: Expired refresh token — client retries after session expiry
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Happy-failure path: the client presents a refresh token that has expired.
    /// The system rejects it cleanly with a null result — the user must re-authenticate.
    /// </summary>
    [Fact]
    public async Task E2E_ExpiredRefreshToken_ValidateAndRevokeReturnsNull()
    {
        // Arrange — store a token that is already expired
        var repo = await CreateRepoAsync();
        var user = UserBuilder.AViewer().Build();
        await SeedUserAsync(user);

        var raw = _tokenService.GenerateRefreshToken();
        var hash = _tokenService.GetRefreshTokenHash(raw);

        // Inject the token directly with a past expiry
        _dbContext!.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1), // already expired
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await repo.ValidateAndRevokeAsync(hash, CancellationToken.None);

        // Assert
        result.Should().BeNull(
            because: "an expired refresh token must not be exchanged for new tokens");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-06: Hash-then-store → Hash-then-compare round-trip
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that hashing the raw token twice (once for storage, once for validation)
    /// always produces the same hash — the foundation of the entire security model.
    /// </summary>
    [Fact]
    public async Task E2E_HashRoundTrip_SameRawTokenHashesToSameValue()
    {
        // Arrange
        var repo = await CreateRepoAsync();
        var user = UserBuilder.AViewer().Build();
        await SeedUserAsync(user);

        // Act — hash once at issuance
        var raw = _tokenService.GenerateRefreshToken();
        var hashAtIssuance = _tokenService.GetRefreshTokenHash(raw);
        await repo.StoreAsync(user.Id, hashAtIssuance, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        // Hash again at validation (simulating the client presenting the raw token)
        var hashAtValidation = _tokenService.GetRefreshTokenHash(raw);

        // Assert — both hashes must match for validation to succeed
        hashAtValidation.Should().Be(hashAtIssuance,
            because: "SHA-256 is deterministic: the same raw token always produces the same hash");

        var result = await repo.ValidateAndRevokeAsync(hashAtValidation, CancellationToken.None);
        result.Should().NotBeNull(
            because: "a matching hash must validate successfully, confirming the round-trip");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-E2E-07: Admin role in token / access token carries role claim
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Admin)]
    public void E2E_GenerateAccessToken_RoleIsCorrectlyEncodedForAllRoles(UserRole role)
    {
        // Arrange
        var user = UserBuilder.AViewer().WithRole(role).Build();

        // Act
        var token = _tokenService.GenerateAccessToken(user);
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — role claim present and correct
        var roleClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role");

        roleClaim.Should().NotBeNull();
        roleClaim!.Value.Should().Be(role.ToString());
    }
}
