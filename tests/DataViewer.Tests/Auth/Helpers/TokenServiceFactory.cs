using DataViewer.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataViewer.Tests.Auth.Helpers;

/// <summary>
/// Creates fully-wired <see cref="TokenService"/> instances for unit tests,
/// managing the <c>JWT__SECRET</c> environment variable lifecycle so individual
/// tests remain hermetic.
/// </summary>
internal static class TokenServiceFactory
{
    /// <summary>
    /// A 32-character (256-bit) secret that satisfies the minimum HS256 requirement.
    /// </summary>
    internal const string ValidSecret = "super-secret-key-at-least-32-byt";

    /// <summary>
    /// Default issuer that matches the built-in <see cref="JwtOptions"/> default.
    /// </summary>
    internal const string DefaultIssuer = "DataViewer";

    /// <summary>
    /// Default audience that matches the built-in <see cref="JwtOptions"/> default.
    /// </summary>
    internal const string DefaultAudience = "DataViewerClients";

    /// <summary>
    /// Default fallback access-token lifetime in minutes.
    /// </summary>
    internal const int DefaultLifetimeMinutes = 15;

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="TokenService"/> with default configuration and the
    /// given secret set in the environment. The environment variable is restored
    /// after the returned <see cref="IDisposable"/> is disposed.
    /// </summary>
    internal static (TokenService service, IDisposable cleanup) Create(
        string secret = ValidSecret,
        string? issuer = null,
        string? audience = null,
        int lifetimeMinutes = DefaultLifetimeMinutes)
    {
        // Set the environment variable required by TokenService.
        var previous = Environment.GetEnvironmentVariable(TokenService.JwtSecretEnvVar);
        Environment.SetEnvironmentVariable(TokenService.JwtSecretEnvVar, secret);

        var config = BuildConfiguration(
            issuer ?? DefaultIssuer,
            audience ?? DefaultAudience,
            lifetimeMinutes);

        var service = new TokenService(config, NullLogger<TokenService>.Instance);

        var cleanup = new EnvironmentVariableRestorer(TokenService.JwtSecretEnvVar, previous);
        return (service, cleanup);
    }

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> backed by an in-memory dictionary.
    /// </summary>
    internal static IConfiguration BuildConfiguration(
        string issuer = DefaultIssuer,
        string audience = DefaultAudience,
        int lifetimeMinutes = DefaultLifetimeMinutes)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = issuer,
                ["Jwt:Audience"] = audience,
                ["Jwt:AccessTokenLifetimeMinutes"] = lifetimeMinutes.ToString(),
            })
            .Build();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores an environment variable to its original value on disposal.
    /// </summary>
    private sealed class EnvironmentVariableRestorer : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;
        private bool _disposed;

        public EnvironmentVariableRestorer(string name, string? previousValue)
        {
            _name = name;
            _previousValue = previousValue;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
