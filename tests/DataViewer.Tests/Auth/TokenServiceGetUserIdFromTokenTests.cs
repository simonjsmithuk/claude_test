using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DataViewer.Domain.Enums;
using DataViewer.Infrastructure.Auth;
using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="TokenService.GetUserIdFromToken"/>.
/// Validates that the method correctly extracts the user ID from a valid token,
/// and returns null (never throws) for invalid, expired, tampered, or malformed tokens.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenServiceGetUserIdFromTokenTests : IDisposable
{
    private readonly TokenService _sut;
    private readonly IDisposable _cleanup;

    public TokenServiceGetUserIdFromTokenTests()
    {
        (_sut, _cleanup) = TokenServiceFactory.Create();
    }

    public void Dispose() => _cleanup.Dispose();

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WithValidToken_ReturnsCorrectUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = UserBuilder.AViewer().WithId(userId).Build();
        var token = _sut.GenerateAccessToken(user);

        // Act
        var result = _sut.GetUserIdFromToken(token);

        // Assert
        result.Should().Be(userId,
            because: "a valid token's sub claim must decode to the user's GUID");
    }

    [Fact]
    public void GetUserIdFromToken_WithValidAdminToken_ReturnsCorrectUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = UserBuilder.AnAdmin().WithId(userId).Build();
        var token = _sut.GenerateAccessToken(user);

        // Act
        var result = _sut.GetUserIdFromToken(token);

        // Assert
        result.Should().Be(userId);
    }

    // ── Null / empty / whitespace input ──────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenTokenIsNull_ReturnsNull()
    {
        // Act
        var result = _sut.GetUserIdFromToken(null!);

        // Assert
        result.Should().BeNull(
            because: "null input is not a valid token and must return null without throwing");
    }

    [Fact]
    public void GetUserIdFromToken_WhenTokenIsEmptyString_ReturnsNull()
    {
        // Act
        var result = _sut.GetUserIdFromToken(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetUserIdFromToken_WhenTokenIsWhitespace_ReturnsNull()
    {
        // Act
        var result = _sut.GetUserIdFromToken("   ");

        // Assert
        result.Should().BeNull();
    }

    // ── Malformed token ───────────────────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenTokenIsMalformed_ReturnsNull()
    {
        // Act
        var result = _sut.GetUserIdFromToken("not.a.jwt");

        // Assert
        result.Should().BeNull(
            because: "malformed tokens must not cause an exception; null is the expected sentinel");
    }

    [Fact]
    public void GetUserIdFromToken_WhenTokenIsArbitraryString_ReturnsNull()
    {
        // Act
        var result = _sut.GetUserIdFromToken("completely-invalid-random-string");

        // Assert
        result.Should().BeNull();
    }

    // ── Expired token ─────────────────────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenTokenIsExpired_ReturnsNull()
    {
        // Arrange — craft an already-expired JWT manually
        var userId = Guid.NewGuid();
        var keyBytes = Encoding.UTF8.GetBytes(TokenServiceFactory.ValidSecret);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiredToken = new JwtSecurityToken(
            issuer: TokenServiceFactory.DefaultIssuer,
            audience: TokenServiceFactory.DefaultAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1), // ← expired 1 hour ago
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(expiredToken);

        // Act
        var result = _sut.GetUserIdFromToken(tokenString);

        // Assert
        result.Should().BeNull(
            because: "an expired token must be rejected and null returned, not throw");
    }

    // ── Wrong signature ───────────────────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenTokenSignedWithDifferentKey_ReturnsNull()
    {
        // Arrange — sign with a completely different key
        var userId = Guid.NewGuid();
        var wrongKeyBytes = Encoding.UTF8.GetBytes("wrong-key-that-is-at-least-32-bytes-long!");
        var key = new SymmetricSecurityKey(wrongKeyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TokenServiceFactory.DefaultIssuer,
            audience: TokenServiceFactory.DefaultAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Act
        var result = _sut.GetUserIdFromToken(tokenString);

        // Assert
        result.Should().BeNull(
            because: "a token signed with a different key must fail signature validation");
    }

    // ── Wrong issuer ──────────────────────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenTokenHasWrongIssuer_ReturnsNull()
    {
        // Arrange — sign with correct key but wrong issuer
        var userId = Guid.NewGuid();
        var keyBytes = Encoding.UTF8.GetBytes(TokenServiceFactory.ValidSecret);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "WrongIssuer",
            audience: TokenServiceFactory.DefaultAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Act
        var result = _sut.GetUserIdFromToken(tokenString);

        // Assert
        result.Should().BeNull(
            because: "a token with an unexpected issuer must be rejected");
    }

    // ── Wrong audience ────────────────────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenTokenHasWrongAudience_ReturnsNull()
    {
        // Arrange — correct key + correct issuer but wrong audience
        var userId = Guid.NewGuid();
        var keyBytes = Encoding.UTF8.GetBytes(TokenServiceFactory.ValidSecret);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TokenServiceFactory.DefaultIssuer,
            audience: "WrongAudience",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Act
        var result = _sut.GetUserIdFromToken(tokenString);

        // Assert
        result.Should().BeNull(
            because: "a token with an unexpected audience must be rejected");
    }

    // ── Missing / non-GUID sub claim ──────────────────────────────────────────

    [Fact]
    public void GetUserIdFromToken_WhenSubClaimIsMissing_ReturnsNull()
    {
        // Arrange — valid token but sub claim omitted
        var keyBytes = Encoding.UTF8.GetBytes(TokenServiceFactory.ValidSecret);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TokenServiceFactory.DefaultIssuer,
            audience: TokenServiceFactory.DefaultAudience,
            claims: new[]
            {
                // No sub claim — intentionally absent
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Act
        var result = _sut.GetUserIdFromToken(tokenString);

        // Assert
        result.Should().BeNull(
            because: "a token without a sub claim cannot yield a user ID");
    }

    [Fact]
    public void GetUserIdFromToken_WhenSubClaimIsNotAGuid_ReturnsNull()
    {
        // Arrange — valid token structure but sub is a non-GUID string
        var keyBytes = Encoding.UTF8.GetBytes(TokenServiceFactory.ValidSecret);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TokenServiceFactory.DefaultIssuer,
            audience: TokenServiceFactory.DefaultAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Act
        var result = _sut.GetUserIdFromToken(tokenString);

        // Assert
        result.Should().BeNull(
            because: "a non-GUID sub claim must not cause an exception; null is returned");
    }

    // ── Non-throwing contract ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a.b.c")]
    [InlineData("garbage")]
    public void GetUserIdFromToken_WithInvalidInput_NeverThrows(string? input)
    {
        // Act
        var act = () => _sut.GetUserIdFromToken(input!);

        // Assert — must return null, not throw
        act.Should().NotThrow(
            because: "GetUserIdFromToken must absorb all validation failures and return null");
    }
}
