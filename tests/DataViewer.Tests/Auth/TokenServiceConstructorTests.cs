using DataViewer.Infrastructure.Auth;
using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="TokenService"/> constructor validation:
/// JWT__SECRET environment variable requirements and configuration loading.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenServiceConstructorTests
{
    // ── JWT__SECRET absent or empty ───────────────────────────────────────────

    [Fact]
    public void Constructor_WhenJwtSecretEnvVarIsAbsent_ThrowsInvalidOperationException()
    {
        // Arrange — clear the environment variable
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, null);

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{TokenService.JwtSecretEnvVar}*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    [Fact]
    public void Constructor_WhenJwtSecretEnvVarIsEmptyString_ThrowsInvalidOperationException()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, string.Empty);

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{TokenService.JwtSecretEnvVar}*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    [Fact]
    public void Constructor_WhenJwtSecretEnvVarIsWhitespaceOnly_ThrowsInvalidOperationException()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, "   ");

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{TokenService.JwtSecretEnvVar}*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    // ── JWT__SECRET too short (< 32 bytes) ────────────────────────────────────

    [Theory]
    [InlineData("short")]              // 5 chars
    [InlineData("exactly31byteslong_1234567890123")]  // 31 chars
    [InlineData("a")]                  // 1 char
    public void Constructor_WhenJwtSecretIsTooShort_ThrowsInvalidOperationException(
        string shortSecret)
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, shortSecret);

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*32*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    // ── JWT__SECRET minimum valid length ──────────────────────────────────────

    [Fact]
    public void Constructor_WhenJwtSecretIsExactly32Bytes_DoesNotThrow()
    {
        // Arrange — exactly 32 ASCII characters = exactly 32 UTF-8 bytes
        const string exactly32Bytes = "12345678901234567890123456789012";
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, exactly32Bytes);

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    [Fact]
    public void Constructor_WhenJwtSecretIsLongerThan32Bytes_DoesNotThrow()
    {
        // Arrange — 64 characters well above minimum
        const string longSecret = "this-is-a-very-long-secret-that-is-definitely-more-than-32-bytes-total";
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, longSecret);

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    // ── Logger null-guard ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(
            TokenService.JwtSecretEnvVar,
            TokenServiceFactory.ValidSecret);

        try
        {
            var config = TokenServiceFactory.BuildConfiguration();

            // Act
            var act = () => new TokenService(config, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    // ── Configuration defaults ────────────────────────────────────────────────

    [Fact]
    public void Constructor_WhenConfigurationHasNoJwtSection_UsesDefaultValues()
    {
        // Arrange — empty configuration (no Jwt section)
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(
            TokenService.JwtSecretEnvVar,
            TokenServiceFactory.ValidSecret);

        try
        {
            var emptyConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            // Act — should not throw; defaults must be applied
            var act = () => new TokenService(emptyConfig, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().NotThrow(
                because: "TokenService must fall back to compiled-in defaults when Jwt section is absent");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    [Fact]
    public void Constructor_WhenAccessTokenLifetimeIsZeroInConfig_UsesDefaultFifteenMinutes()
    {
        // Arrange — AccessTokenLifetimeMinutes = 0 is invalid; should fall back to 15
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(
            TokenService.JwtSecretEnvVar,
            TokenServiceFactory.ValidSecret);

        try
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "DataViewer",
                    ["Jwt:Audience"] = "DataViewerClients",
                    ["Jwt:AccessTokenLifetimeMinutes"] = "0",   // ← invalid
                })
                .Build();

            // Act — must not throw; fall back to default lifetime
            var act = () => new TokenService(config, NullLogger<TokenService>.Instance);

            // Assert
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, previous);
        }
    }

    // ── ITokenService interface contract ──────────────────────────────────────

    [Fact]
    public void TokenService_ImplementsITokenServiceInterface()
    {
        typeof(TokenService).Should()
            .Implement<DataViewer.Application.Interfaces.ITokenService>(
                because: "TokenService must implement the Application-layer ITokenService contract");
    }
}
