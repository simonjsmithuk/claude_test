using DataViewer.Tests.Auth.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DataViewer.Tests.Auth;

/// <summary>
/// Unit tests for <c>JwtOptions.FromConfiguration</c> — exercised indirectly through
/// <see cref="DataViewer.Infrastructure.Auth.TokenService"/> since <c>JwtOptions</c>
/// is an <c>internal sealed</c> class.  These tests verify that the configuration
/// fallback behaviour and boundary conditions are correct.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JwtOptionsTests : IDisposable
{
    private readonly IDisposable _cleanup;

    public JwtOptionsTests()
    {
        // Ensure the environment variable is set so TokenService construction succeeds
        var previous = Environment.GetEnvironmentVariable(
            DataViewer.Infrastructure.Auth.TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(
            DataViewer.Infrastructure.Auth.TokenService.JwtSecretEnvVar,
            TokenServiceFactory.ValidSecret);

        _cleanup = new EnvironmentRestorer(
            DataViewer.Infrastructure.Auth.TokenService.JwtSecretEnvVar, previous);
    }

    public void Dispose() => _cleanup.Dispose();

    // ── Issuer fallback ───────────────────────────────────────────────────────

    [Fact]
    public void TokenService_WhenIssuerNotInConfig_UsesDefaultDataViewerIssuer()
    {
        // Arrange — configuration without Jwt:Issuer
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // No Jwt:Issuer key
                ["Jwt:Audience"] = "SomeAudience",
                ["Jwt:AccessTokenLifetimeMinutes"] = "30",
            })
            .Build();

        var service = new DataViewer.Infrastructure.Auth.TokenService(
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                DataViewer.Infrastructure.Auth.TokenService>.Instance);

        // Act
        var user = UserBuilder.AViewer().Build();
        var token = service.GenerateAccessToken(user);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token);

        // Assert
        jwt.Issuer.Should().Be("DataViewer",
            because: "the compiled-in default issuer is 'DataViewer'");
    }

    [Fact]
    public void TokenService_WhenAudienceNotInConfig_UsesDefaultDataViewerClientsAudience()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "DataViewer",
                // No Jwt:Audience key
                ["Jwt:AccessTokenLifetimeMinutes"] = "30",
            })
            .Build();

        var service = new DataViewer.Infrastructure.Auth.TokenService(
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                DataViewer.Infrastructure.Auth.TokenService>.Instance);

        // Act
        var user = UserBuilder.AViewer().Build();
        var token = service.GenerateAccessToken(user);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token);

        // Assert
        jwt.Audiences.Should().Contain("DataViewerClients",
            because: "the compiled-in default audience is 'DataViewerClients'");
    }

    // ── AccessTokenLifetimeMinutes boundary values ────────────────────────────

    [Theory]
    [InlineData("0")]          // zero → invalid → fallback to 15
    [InlineData("-1")]         // negative → invalid → fallback to 15
    [InlineData("not-a-number")] // non-integer → invalid → fallback to 15
    [InlineData("")]           // empty string → invalid → fallback to 15
    public void TokenService_WhenAccessTokenLifetimeIsInvalid_FallsBackTo15MinuteDefault(
        string badValue)
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "DataViewer",
                ["Jwt:Audience"] = "DataViewerClients",
                ["Jwt:AccessTokenLifetimeMinutes"] = badValue,
            })
            .Build();

        var service = new DataViewer.Infrastructure.Auth.TokenService(
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                DataViewer.Infrastructure.Auth.TokenService>.Instance);

        // Act — token must expire ~15 minutes from now (the default)
        var user = UserBuilder.AViewer().Build();
        var before = DateTime.UtcNow;
        var token = service.GenerateAccessToken(user);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token);

        // Assert — expiry should be ~15 minutes, not some other value
        var expiry = jwt.ValidTo;
        expiry.Should().BeAfter(before.AddMinutes(14),
            because: "fallback lifetime is 15 minutes");
        expiry.Should().BeBefore(before.AddMinutes(16),
            because: "fallback lifetime is 15 minutes");
    }

    [Fact]
    public void TokenService_WhenAccessTokenLifetimeIsPositiveInteger_UsesConfiguredValue()
    {
        // Arrange
        const int customMinutes = 45;
        var config = TokenServiceFactory.BuildConfiguration(lifetimeMinutes: customMinutes);
        var service = new DataViewer.Infrastructure.Auth.TokenService(
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                DataViewer.Infrastructure.Auth.TokenService>.Instance);

        // Act
        var user = UserBuilder.AViewer().Build();
        var before = DateTime.UtcNow;
        var token = service.GenerateAccessToken(user);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token);

        // Assert
        var expiry = jwt.ValidTo;
        expiry.Should().BeAfter(before.AddMinutes(44))
            .And.BeBefore(before.AddMinutes(46),
                because: $"configured lifetime of {customMinutes} minutes must be honoured");
    }

    // ── Private helper ────────────────────────────────────────────────────────

    private sealed class EnvironmentRestorer : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentRestorer(string name, string? previous)
        {
            _name = name;
            _previous = previous;
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable(_name, _previous);
    }
}
