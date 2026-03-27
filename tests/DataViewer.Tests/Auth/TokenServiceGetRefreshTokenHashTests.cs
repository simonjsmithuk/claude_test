using DataViewer.Infrastructure.Auth;
using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="TokenService.GetRefreshTokenHash"/>.
/// Validates SHA-256 hashing, determinism, output format (64-char lowercase hex),
/// and input guard conditions specified in TASK-014.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenServiceGetRefreshTokenHashTests : IDisposable
{
    private readonly TokenService _sut;
    private readonly IDisposable _cleanup;

    public TokenServiceGetRefreshTokenHashTests()
    {
        (_sut, _cleanup) = TokenServiceFactory.Create();
    }

    public void Dispose() => _cleanup.Dispose();

    // ── Input guards ──────────────────────────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_WhenTokenIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetRefreshTokenHash(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("token");
    }

    [Fact]
    public void GetRefreshTokenHash_WhenTokenIsEmptyString_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetRefreshTokenHash(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("token");
    }

    [Fact]
    public void GetRefreshTokenHash_WhenTokenIsWhitespaceOnly_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetRefreshTokenHash("   ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("token");
    }

    // ── Output format ─────────────────────────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_WithValidToken_Returns64CharacterString()
    {
        // Arrange — SHA-256 produces 32 bytes → 64 hex characters
        var rawToken = _sut.GenerateRefreshToken();

        // Act
        var hash = _sut.GetRefreshTokenHash(rawToken);

        // Assert
        hash.Should().HaveLength(64,
            because: "SHA-256 output is 256 bits = 32 bytes = 64 hex characters");
    }

    [Fact]
    public void GetRefreshTokenHash_WithValidToken_ReturnsLowercaseHexString()
    {
        // Arrange
        var rawToken = _sut.GenerateRefreshToken();

        // Act
        var hash = _sut.GetRefreshTokenHash(rawToken);

        // Assert — every character must be a lowercase hex digit
        hash.Should().MatchRegex("^[0-9a-f]{64}$",
            because: "token hash must be lowercase hex to match the TokenHash column convention");
    }

    [Fact]
    public void GetRefreshTokenHash_WithValidToken_ContainsNoUppercaseLetters()
    {
        // Arrange
        var rawToken = _sut.GenerateRefreshToken();

        // Act
        var hash = _sut.GetRefreshTokenHash(rawToken);

        // Assert
        hash.Should().Be(hash.ToLowerInvariant(),
            because: "hash must be all-lowercase per acceptance criteria");
    }

    // ── SHA-256 correctness ────────────────────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_WithKnownInput_ReturnsExpectedSha256Hash()
    {
        // Arrange — pre-computed SHA-256 of "hello" in lowercase hex
        // echo -n "hello" | sha256sum → 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        const string input = "hello";
        const string expected = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

        // Act
        var hash = _sut.GetRefreshTokenHash(input);

        // Assert
        hash.Should().Be(expected,
            because: "the SHA-256 hash of 'hello' (UTF-8) must match the well-known value");
    }

    [Fact]
    public void GetRefreshTokenHash_WithAnotherKnownInput_ReturnsExpectedSha256Hash()
    {
        // Arrange — SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        // But empty string is rejected by the guard; use a different known value.
        // SHA-256("DataViewer") verified externally.
        const string input = "DataViewer";
        var expectedBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        // Act
        var hash = _sut.GetRefreshTokenHash(input);

        // Assert
        hash.Should().Be(expected);
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_CalledTwiceWithSameToken_ReturnsSameHash()
    {
        // Arrange
        var rawToken = _sut.GenerateRefreshToken();

        // Act
        var hash1 = _sut.GetRefreshTokenHash(rawToken);
        var hash2 = _sut.GetRefreshTokenHash(rawToken);

        // Assert — SHA-256 is deterministic
        hash1.Should().Be(hash2,
            because: "the same raw token must always produce the same hash");
    }

    // ── Collision resistance ──────────────────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_WithDifferentTokens_ReturnsDifferentHashes()
    {
        // Arrange
        var token1 = _sut.GenerateRefreshToken();
        var token2 = _sut.GenerateRefreshToken();

        // Act
        var hash1 = _sut.GetRefreshTokenHash(token1);
        var hash2 = _sut.GetRefreshTokenHash(token2);

        // Assert — different inputs must produce different hashes (SHA-256 is collision resistant)
        hash1.Should().NotBe(hash2,
            because: "different refresh tokens must produce different hashes");
    }

    // ── Hash-then-compare workflow ────────────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_HashOfRawToken_MatchesHashComputedIndependently()
    {
        // Arrange — compute the hash independently using BCL
        var rawToken = _sut.GenerateRefreshToken();
        var expectedHashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken));
        var expectedHash = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        // Act
        var actualHash = _sut.GetRefreshTokenHash(rawToken);

        // Assert
        actualHash.Should().Be(expectedHash,
            because: "GetRefreshTokenHash must use SHA-256 over the UTF-8 encoded raw token");
    }

    // ── Boundary: single-character token ─────────────────────────────────────

    [Fact]
    public void GetRefreshTokenHash_WithSingleCharacterToken_ReturnsValid64CharHash()
    {
        // Arrange
        const string singleChar = "x";

        // Act
        var hash = _sut.GetRefreshTokenHash(singleChar);

        // Assert
        hash.Should().HaveLength(64)
            .And.MatchRegex("^[0-9a-f]{64}$");
    }

    // ── Parameterised: various raw token inputs ───────────────────────────────

    [Theory]
    [InlineData("some-raw-token-value")]
    [InlineData("another-token-abc123")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ab")]
    public void GetRefreshTokenHash_WithVariousValidInputs_AlwaysReturns64LowercaseHexChars(
        string input)
    {
        // Act
        var hash = _sut.GetRefreshTokenHash(input);

        // Assert
        hash.Should().HaveLength(64)
            .And.MatchRegex("^[0-9a-f]{64}$",
                because: "SHA-256 always produces 256-bit (64 hex char) output in lowercase");
    }
}
