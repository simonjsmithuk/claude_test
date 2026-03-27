using DataViewer.Infrastructure.Auth;
using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="TokenService.GenerateRefreshToken"/>.
/// Validates cryptographic randomness requirements, Base64 encoding, token length,
/// and uniqueness properties specified in TASK-014.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenServiceGenerateRefreshTokenTests : IDisposable
{
    private readonly TokenService _sut;
    private readonly IDisposable _cleanup;

    public TokenServiceGenerateRefreshTokenTests()
    {
        (_sut, _cleanup) = TokenServiceFactory.Create();
    }

    public void Dispose() => _cleanup.Dispose();

    // ── Return value: not null or empty ───────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyString()
    {
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrWhiteSpace(
            because: "a refresh token must be a non-empty Base64 string");
    }

    // ── Base64 encoding validation ────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_ReturnsValidBase64String()
    {
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert — Convert.FromBase64String throws FormatException if not valid Base64
        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow(
            because: "the refresh token must be a valid Base64-encoded string");
    }

    // ── 64-byte source material ───────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_DecodesTo64Bytes()
    {
        // Arrange — 64 random bytes encode to 88 Base64 characters (64 * 4/3, padded)
        // Act
        var token = _sut.GenerateRefreshToken();
        var decoded = Convert.FromBase64String(token);

        // Assert
        decoded.Should().HaveCount(64,
            because: "acceptance criteria specifies 64-byte (512-bit) cryptographic random output");
    }

    [Fact]
    public void GenerateRefreshToken_EncodedStringIs88CharactersLong()
    {
        // Base64 of 64 bytes = ceil(64/3)*4 = 88 characters (with padding '=')
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert
        token.Should().HaveLength(88,
            because: "Base64 encoding of 64 bytes produces exactly 88 characters");
    }

    // ── Uniqueness / randomness ───────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_CalledTwice_ProducesDifferentTokens()
    {
        // Act
        var token1 = _sut.GenerateRefreshToken();
        var token2 = _sut.GenerateRefreshToken();

        // Assert
        token1.Should().NotBe(token2,
            because: "each refresh token must be independently random");
    }

    [Fact]
    public void GenerateRefreshToken_CalledManyTimes_AllTokensAreUnique()
    {
        // Arrange
        const int count = 100;

        // Act
        var tokens = Enumerable.Range(0, count)
            .Select(_ => _sut.GenerateRefreshToken())
            .ToList();

        // Assert — all tokens must be distinct (collisions are astronomically unlikely)
        tokens.Distinct().Should().HaveCount(count,
            because: "cryptographically random 64-byte tokens must not collide");
    }

    [Fact]
    public void GenerateRefreshToken_CalledManyTimes_DecodedBytesAreNotAllZeros()
    {
        // Act
        var token = _sut.GenerateRefreshToken();
        var bytes = Convert.FromBase64String(token);

        // Assert — all-zero bytes would indicate a defective RNG
        bytes.Should().Contain(b => b != 0,
            because: "a cryptographically-random 64-byte token must not consist entirely of zero bytes");
    }

    // ── No trailing newlines or whitespace ────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_DoesNotContainWhitespace()
    {
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert — Base64 strings used as tokens must not contain whitespace
        token.Should().NotContain(" ");
        token.Should().NotContain("\n");
        token.Should().NotContain("\r");
    }
}
