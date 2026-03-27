using DataViewer.Application.Interfaces;
using DataViewer.Infrastructure.Encryption;
using DataViewer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataViewer.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> that register all
/// Infrastructure-layer services with the DI container.
/// </summary>
/// <remarks>
/// <para>
/// Call this method from the API project's composition root (<c>Program.cs</c>)
/// after <c>WebApplication.CreateBuilder(args)</c>:
/// <code>
/// builder.Services.AddInfrastructure(builder.Configuration);
/// </code>
/// </para>
/// <para>
/// The API project must never reference Infrastructure concrete types directly
/// (Clean Architecture rule). All service bindings are declared here and consumed
/// through the interfaces defined in <c>DataViewer.Application.Interfaces</c>.
/// </para>
/// </remarks>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers all Infrastructure-layer services into <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">
    /// The application configuration used to resolve the connection string and
    /// database provider name.
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/> instance to support method chaining.
    /// </returns>
    /// <remarks>
    /// <strong>Encryption service lifetime — Singleton:</strong>
    /// <see cref="AesEncryptionService"/> is registered as a Singleton because:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       The AES key bytes are decoded from the environment variable once at
    ///       construction time and cached — re-reading on every request would be
    ///       wasteful and create an inconsistency window if the variable changes at runtime.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       All encryption/decryption operations are stateless beyond the immutable
    ///       key field, and <see cref="System.Security.Cryptography.AesGcm"/> instances
    ///       are created per-call rather than shared, making the Singleton safe for
    ///       concurrent use without locking.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <strong>AppDbContext lifetime — Scoped (EF Core default):</strong>
    /// DbContext is inherently not thread-safe and is designed for a single unit-of-work
    /// per HTTP request. Scoped lifetime matches this design exactly.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Encryption ────────────────────────────────────────────────────────
        // AesEncryptionService reads DATAVIEWER_ENCRYPTION_KEY at construction time
        // and throws InvalidOperationException if the variable is missing or invalid.
        // With Singleton lifetime this validation runs exactly once — at startup —
        // ensuring the application fails fast with a clear error rather than
        // encountering a missing key mid-request.
        services.AddSingleton<IEncryptionService, AesEncryptionService>();

        // ── Database ──────────────────────────────────────────────────────────
        ConfigureDbContext(services, configuration);

        return services;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the database provider and connection string from configuration and
    /// registers <see cref="AppDbContext"/> with the appropriate EF Core provider.
    /// </summary>
    /// <remarks>
    /// Provider selection is driven by the <c>"DatabaseProvider"</c> appsettings key:
    /// <list type="table">
    ///   <listheader><term>Value</term><term>Provider</term></listheader>
    ///   <item><term>postgresql</term><term>Npgsql (PostgreSQL ≥ 14)</term></item>
    ///   <item><term>mysql</term><term>Pomelo (MySQL ≥ 8.0.13)</term></item>
    /// </list>
    /// No code change is required to switch databases — only the appsettings value
    /// and connection string need to change (Product Spec G-05).
    ///
    /// <para>
    /// <see cref="AppDbContext"/> requires the provider string as a constructor
    /// argument so that entity configurations can emit the correct provider-specific
    /// column type annotations at model-build time (e.g. <c>jsonb</c> vs <c>JSON</c>
    /// for the <c>AuditLogEntry.Parameters</c> column).
    ///
    /// The <c>AddDbContext</c> overload that accepts a <c>(IServiceProvider, DbContextOptionsBuilder)</c>
    /// factory delegate is used so that both the provider-specific EF Core options AND
    /// the extra <c>databaseProvider</c> constructor argument are threaded into a single
    /// <see cref="AppDbContext"/> registration without a second <c>AddScoped</c> call.
    /// This avoids the previous double-registration pattern (two entries for the same
    /// type in the service descriptor list) and does not rely on undocumented DI
    /// container last-writer-wins behaviour.
    /// </para>
    ///
    /// <para>
    /// EF Core's internal model cache still ensures the compiled model is built only
    /// once, even though <c>DbContextOptions</c> is no longer registered as a separate
    /// Singleton. The options builder lambda is invoked per-scope but the expensive
    /// model compilation is cached by the provider's <c>IModelCacheKeyFactory</c>.
    /// </para>
    /// </remarks>
    private static void ConfigureDbContext(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = (configuration["DatabaseProvider"] ?? "postgresql")
            .Trim()
            .ToLowerInvariant();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required but was not found in configuration. "
                + "Set the DATAVIEWER_DB_CONNECTION environment variable or provide it in appsettings.");

        // Single AddDbContext call that captures both the provider-specific EF Core options
        // and the databaseProvider constructor argument in one factory delegate.
        // This replaces the previous two-step pattern (AddDbContext + AddScoped replacement)
        // which produced an orphaned service descriptor and relied on last-writer-wins.
        services.AddDbContext<AppDbContext>((_, dbContextOptions) =>
            ConfigureProviderOptions(dbContextOptions, provider, connectionString));
    }

    /// <summary>
    /// Configures the provider-specific EF Core options on <paramref name="dbContextOptions"/>
    /// based on the supplied <paramref name="provider"/> name.
    /// </summary>
    /// <remarks>
    /// <strong>MySQL server version — pinned, not auto-detected:</strong>
    /// <c>ServerVersion.AutoDetect(connectionString)</c> opens a real database connection
    /// during DI container construction (before the application is ready to serve requests).
    /// If the database is unavailable at startup this throws during <c>WebApplication.Build()</c>.
    /// In Docker Compose environments the app container often starts before the database
    /// container is healthy, making auto-detect unreliable. A pinned <c>MySqlServerVersion</c>
    /// eliminates the live connection requirement and ensures predictable cold-start behaviour.
    /// The minimum supported MySQL version is 8.0.13 (required for functional/partial indexes
    /// used by <c>CredentialProfileConfiguration</c>).
    /// </remarks>
    private static void ConfigureProviderOptions(
        DbContextOptionsBuilder dbContextOptions,
        string provider,
        string connectionString)
    {
        if (provider == "mysql")
        {
            // Pomelo.EntityFrameworkCore.MySql — MIT license
            // Pinned to MySQL 8.0.13 — the minimum version that supports functional index
            // expressions required for the idx_profile_name partial index.
            // Do NOT use ServerVersion.AutoDetect: it opens a live DB connection at
            // startup, which fails when the database is not yet ready (e.g. Docker Compose).
            var mysqlVersion = new MySqlServerVersion(new Version(8, 0, 13));

            dbContextOptions.UseMySql(
                connectionString,
                mysqlVersion,
                mysqlOptions =>
                {
                    mysqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                });
        }
        else
        {
            // Default: Npgsql / PostgreSQL — PostgreSQL License (permissive, BSD-like)
            dbContextOptions.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
        }
    }
}
