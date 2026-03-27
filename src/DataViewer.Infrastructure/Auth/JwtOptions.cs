using Microsoft.Extensions.Configuration;

namespace DataViewer.Infrastructure.Auth;

/// <summary>
/// Strongly-typed holder for JWT configuration values read from the
/// <c>Jwt</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is an internal value object used exclusively by <see cref="TokenService"/>.
/// It is not registered in the DI container as an <c>IOptions&lt;T&gt;</c> because
/// <see cref="TokenService"/> is registered as a Singleton and reads configuration
/// once at construction time — the <c>IOptions</c> pattern would not add value here
/// and would introduce an unnecessary infrastructure dependency.
/// </para>
///
/// <para>
/// Token lifetime values serve as <em>static fallbacks</em> when the dynamic
/// <see cref="Domain.Entities.SystemSettings"/> row is not available. The
/// application-layer use-cases are expected to resolve the runtime
/// <see cref="Domain.Entities.SystemSettings.JwtAccessTokenMinutes"/> from
/// <see cref="ISystemSettingsRepository"/> and supply it when generating tokens.
/// </para>
/// </remarks>
internal sealed class JwtOptions
{
    // Configuration section key — matches the "Jwt" section in appsettings.json.
    private const string SectionKey = "Jwt";

    /// <summary>
    /// Token issuer embedded in the <c>iss</c> JWT claim.
    /// Default: <c>"DataViewer"</c> (matches appsettings.json default).
    /// </summary>
    public string Issuer { get; private init; } = "DataViewer";

    /// <summary>
    /// Token audience embedded in the <c>aud</c> JWT claim.
    /// Default: <c>"DataViewerClients"</c> (matches appsettings.json default).
    /// </summary>
    public string Audience { get; private init; } = "DataViewerClients";

    /// <summary>
    /// Fallback access-token lifetime in minutes, used when the dynamic
    /// <see cref="Domain.Entities.SystemSettings"/> value is unavailable.
    /// Default: 15 minutes (matches appsettings.json default).
    /// </summary>
    public int FallbackAccessTokenLifetimeMinutes { get; private init; } = 15;

    /// <summary>
    /// Reads the <c>Jwt</c> section from <paramref name="configuration"/> and
    /// returns a populated <see cref="JwtOptions"/> instance.
    /// </summary>
    /// <param name="configuration">
    /// The application configuration graph (appsettings.json + environment variables).
    /// </param>
    /// <returns>
    /// A <see cref="JwtOptions"/> instance with values from configuration, falling
    /// back to compiled-in defaults for any missing key.
    /// </returns>
    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionKey);

        // Parse AccessTokenLifetimeMinutes defensively; fall back to the
        // compiled-in default (15) when the key is absent or not a valid integer.
        if (!int.TryParse(
                section["AccessTokenLifetimeMinutes"],
                out var accessTokenMinutes) || accessTokenMinutes <= 0)
        {
            accessTokenMinutes = 15;
        }

        return new JwtOptions
        {
            Issuer = section["Issuer"] ?? "DataViewer",
            Audience = section["Audience"] ?? "DataViewerClients",
            FallbackAccessTokenLifetimeMinutes = accessTokenMinutes,
        };
    }
}
