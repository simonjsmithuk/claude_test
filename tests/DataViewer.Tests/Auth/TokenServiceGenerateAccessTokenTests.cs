using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DataViewer.Domain.Enums;
using DataViewer.Infrastructure.Auth;
using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="TokenService.GenerateAccessToken"/>.
/// Validates JWT structure, claims payload, signing algorithm, expiry, issuer,
/// and audience against the acceptance criteria for TASK-014.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenServiceGenerateAccessTokenTests : IDisposable
{
    private readonly TokenService _sut;
    private readonly IDisposable _cleanup;

    // Shared validation parameters used across multiple tests
    private readonly TokenValidationParameters _validationParameters;

    public TokenServiceGenerateAccessTokenTests()
    {
        (_sut, _cleanup) = TokenServiceFactory.Create();

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(TokenServiceFactory.ValidSecret)),
            ValidateIssuer = true,
            ValidIssuer = TokenServiceFactory.DefaultIssuer,
            ValidateAudience = true,
            ValidAudience = TokenServiceFactory.DefaultAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RequireExpirationTime = true,
        };
    }

    public void Dispose() => _cleanup.Dispose();

    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WhenUserIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.GenerateAccessToken(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("user");
    }

    // ── Return value basic structure ──────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ReturnsNonEmptyString()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateAccessToken_WithValidUser_ReturnsCompactJwtWithThreeParts()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert — compact JWT = header.payload.signature (exactly 3 dot-separated parts)
        token.Split('.').Should().HaveCount(3,
            because: "a compact serialised JWT always has exactly 3 Base64url-encoded segments");
    }

    // ── Signature validation ──────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ProducesValidlySignedToken()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert — full validation: signature + issuer + audience + lifetime
        var act = () => handler.ValidateToken(token, _validationParameters, out _);
        act.Should().NotThrow(because: "the generated token must pass all validation checks");
    }

    [Fact]
    public void GenerateAccessToken_TokenSignedWithWrongKey_FailsValidation()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var token = _sut.GenerateAccessToken(user);
        var handler = new JwtSecurityTokenHandler();

        var wrongKeyParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes("this-is-a-wrong-key-that-is-32-bytes!")),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
        };

        // Act
        var act = () => handler.ValidateToken(token, wrongKeyParams, out _);

        // Assert
        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>(
            because: "a token signed with a different key must fail signature validation");
    }

    [Fact]
    public void GenerateAccessToken_WithValidUser_UsesHmacSha256Algorithm()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        jwt.Header.Alg.Should().Be("HS256",
            because: "the acceptance criteria requires HS256 signing");
    }

    // ── Claims: sub (user ID) ─────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ContainsSubClaimEqualToUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = UserBuilder.AViewer().WithId(userId).Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        sub.Should().Be(userId.ToString(),
            because: "sub claim must equal the user's GUID as a string");
    }

    // ── Claims: role ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Viewer, "Viewer")]
    [InlineData(UserRole.Admin, "Admin")]
    public void GenerateAccessToken_WithUserRole_ContainsRoleClaim(
        UserRole role, string expectedRoleString)
    {
        // Arrange
        var user = UserBuilder.AViewer().WithRole(role).Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert — role claim maps to ClaimTypes.Role which is the long-form URI string
        var roleClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == ClaimTypes.Role ||
            c.Type == "role");
        roleClaim.Should().NotBeNull(because: "a role claim must be present in the JWT");
        roleClaim!.Value.Should().Be(expectedRoleString,
            because: "role claim value must match the UserRole enum name");
    }

    // ── Claims: jti (unique token ID) ─────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ContainsJtiClaim()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
        jti.Should().NotBeNullOrWhiteSpace(because: "jti claim must be present");
        Guid.TryParse(jti, out _).Should().BeTrue(
            because: "jti must be a valid GUID string (per acceptance criteria)");
    }

    [Fact]
    public void GenerateAccessToken_CalledTwiceForSameUser_ProducesDifferentJtiValues()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token1 = _sut.GenerateAccessToken(user);
        var token2 = _sut.GenerateAccessToken(user);
        var jti1 = handler.ReadJwtToken(token1).Claims
            .First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = handler.ReadJwtToken(token2).Claims
            .First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        // Assert — each generated token must have a unique jti
        jti1.Should().NotBe(jti2,
            because: "each access token must have a unique jti to support per-token revocation");
    }

    // ── Claims: exp (expiry) ──────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithDefaultLifetime_TokenExpiresInApproximately15Minutes()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();
        var before = DateTime.UtcNow;

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        var expiry = jwt.ValidTo;
        expiry.Should().BeAfter(before.AddMinutes(14),
            because: "token should not expire before ~15 minutes");
        expiry.Should().BeBefore(before.AddMinutes(16),
            because: "token should expire within ~15 minutes, not later");
    }

    [Fact]
    public void GenerateAccessToken_WithCustomLifetime_TokenExpiresAfterConfiguredMinutes()
    {
        // Arrange
        const int customLifetime = 60;
        (var service, var cleanup) = TokenServiceFactory.Create(lifetimeMinutes: customLifetime);

        try
        {
            var user = UserBuilder.AViewer().Build();
            var handler = new JwtSecurityTokenHandler();
            var before = DateTime.UtcNow;

            // Act
            var token = service.GenerateAccessToken(user);
            var jwt = handler.ReadJwtToken(token);

            // Assert — expiry should be ~60 minutes from now
            var expiry = jwt.ValidTo;
            expiry.Should().BeAfter(before.AddMinutes(59),
                because: $"token should not expire before ~{customLifetime} minutes");
            expiry.Should().BeBefore(before.AddMinutes(61),
                because: $"token should expire within ~{customLifetime} minutes, not later");
        }
        finally
        {
            cleanup.Dispose();
        }
    }

    [Fact]
    public void GenerateAccessToken_WithValidUser_TokenIsNotExpiredImmediately()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert — full validation must pass right after generation
        var act = () => handler.ValidateToken(token, _validationParameters, out _);
        act.Should().NotThrow(because: "a freshly generated token must not be considered expired");
    }

    // ── Claims: iss (issuer) ──────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ContainsCorrectIssuer()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        jwt.Issuer.Should().Be(TokenServiceFactory.DefaultIssuer,
            because: "iss claim must match the configured Jwt:Issuer value");
    }

    [Fact]
    public void GenerateAccessToken_WithCustomIssuer_EmbeddsCustomIssuerInToken()
    {
        // Arrange
        const string customIssuer = "CustomIssuer";
        (var service, var cleanup) = TokenServiceFactory.Create(issuer: customIssuer);

        try
        {
            var user = UserBuilder.AViewer().Build();
            var handler = new JwtSecurityTokenHandler();

            // Act
            var token = service.GenerateAccessToken(user);
            var jwt = handler.ReadJwtToken(token);

            // Assert
            jwt.Issuer.Should().Be(customIssuer);
        }
        finally
        {
            cleanup.Dispose();
        }
    }

    // ── Claims: aud (audience) ────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ContainsCorrectAudience()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        jwt.Audiences.Should().Contain(TokenServiceFactory.DefaultAudience,
            because: "aud claim must match the configured Jwt:Audience value");
    }

    // ── Claims: name (username) ───────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_ContainsNameClaimEqualToUserName()
    {
        // Arrange
        const string expectedUserName = "alice";
        var user = UserBuilder.AViewer().WithUserName(expectedUserName).Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert
        var nameClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == JwtRegisteredClaimNames.Name ||
            c.Type == "name")?.Value;

        nameClaim.Should().Be(expectedUserName,
            because: "name claim must equal User.UserName");
    }

    // ── Two different users produce different tokens ───────────────────────────

    [Fact]
    public void GenerateAccessToken_ForDifferentUsers_ProducesDifferentTokens()
    {
        // Arrange
        var user1 = UserBuilder.AViewer().WithId(Guid.NewGuid()).WithUserName("alice").Build();
        var user2 = UserBuilder.AnAdmin().WithId(Guid.NewGuid()).WithUserName("bob").Build();

        // Act
        var token1 = _sut.GenerateAccessToken(user1);
        var token2 = _sut.GenerateAccessToken(user2);

        // Assert
        token1.Should().NotBe(token2,
            because: "tokens for different users must differ");
    }

    // ── nbf (not-before) ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_WithValidUser_TokenIsValidImmediately()
    {
        // Arrange
        var user = UserBuilder.AViewer().Build();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _sut.GenerateAccessToken(user);
        var jwt = handler.ReadJwtToken(token);

        // Assert — ValidFrom (nbf) should be close to now
        jwt.ValidFrom.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10),
            because: "nbf must be approximately the current time so the token is usable immediately");
    }
}
