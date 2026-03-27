using DataViewer.Application.Interfaces;
using DataViewer.Infrastructure.Encryption;
using DataViewer.Infrastructure.Persistence;
using DataViewer.Infrastructure.Persistence.Repositories;
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
    /// <strong>Repository lifetimes — Scoped:</strong>
    /// Repositories take a constructor dependency on <see cref="AppDbContext"/>, which
    /// is Scoped (one per HTTP request). Repositories must therefore also be Scoped to
    /// avoid consuming a shorter-lived dependency from a longer-lived container.
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

        // ── Repositories ──────────────────────────────────────────────────────
        // Scoped to match the AppDbContext lifetime (one unit-of-work per request).
        // Bindings are to the Application interfaces — Infrastructure concrete types
        // are never referenced from other layers (Clean Architecture rule).
        RegisterRepositories(services);

        return services;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Binds all repository interfaces to their Infrastructure implementations.
    /// </summary>
    /// <remarks>
    /// Each repository is registered as Scoped because repositories hold a constructor
    /// dependency on <see cref="AppDbContext"/>, which is itself Scoped.
    /// Registering as Singleton would capture a short-lived DbContext inside a
    /// long-lived container, causing stale data and thread-safety issues.
    /// </remarks>
    private static void RegisterRepositories(IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICredentialProfileRepository, CredentialProfileRepository>();
    }

    /// <summary>
    /// Resolves the database provider and connection string from configuration and
    /// registers <see cref="AppDbContext"/> using a custom scoped factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provider selection is driven by the <c>"DatabaseProvider"</c> appsettings key
    /// and is delegated entirely to <see cref="DatabaseProviderFactory.Configure"/>,
    /// which enforces the supported-value contract (<c>"postgresql"</c> | <c>"mysql"</c>)
    /// and throws <see cref="InvalidOperationException"/> for unrecognised values.
    /// The fail-fast behaviour happens during DI registration (host build) rather than
    /// during the first database operation, which is the desired startup-validation pattern.
    /// </para>
    ///
    /// <para>
    /// <strong>Why a two-step registration is used here:</strong>
    /// <see cref="AppDbContext"/> requires a plain <c>string databaseProvider</c>
    /// as its second constructor parameter. This is a configuration value, not a
    /// registered service, so the standard <c>AddDbContext&lt;T&gt;()</c> auto-wiring
    /// cannot resolve it from the DI container without additional ceremony.
    ///
    /// The chosen pattern:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <c>AddDbContext</c> with a <c>(IServiceProvider, DbContextOptionsBuilder)</c>
    ///       factory delegate configures the <c>DbContextOptions&lt;AppDbContext&gt;</c>
    ///       Singleton (via <see cref="DatabaseProviderFactory.Configure"/>) and registers
    ///       <see cref="AppDbContext"/> as a Scoped service backed by those options.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The two-argument <c>AddDbContext</c> overload with the
    ///       <c>(IServiceProvider, DbContextOptionsBuilder)</c> factory is the standard
    ///       EF Core hook for this scenario and does NOT produce a duplicate service descriptor.
    ///       The <c>provider</c> string is captured in the closure and forwarded to
    ///       <see cref="AppDbContext"/>'s constructor via a custom
    ///       <c>IDbContextOptionsExtension</c>-compatible path — specifically by building
    ///       <c>DbContextOptions</c> with the provider annotation stored on the options object,
    ///       and then using the options-builder overload of <see cref="AppDbContext"/> that
    ///       accepts both the options and the provider name.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// In practice, EF Core's <c>AddDbContext</c> registers the context factory as
    /// a Scoped delegate. We override that with a single explicit Scoped registration
    /// that passes the captured <c>provider</c> string directly to the constructor —
    /// this is the cleanest approach that avoids the orphaned-descriptor problem of
    /// calling both <c>AddDbContext</c> and <c>AddScoped</c> for the same type.
    /// </para>
    ///
    /// <para>
    /// EF Core's internal model cache ensures the compiled model is built only once,
    /// even with a per-scope factory. The expensive model compilation is cached by the
    /// provider's <c>IModelCacheKeyFactory</c>.
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

        // Validate the provider value eagerly at startup (fail-fast).
        // DatabaseProviderFactory.Configure throws InvalidOperationException for unknown values.
        // Build a temporary throwaway options object solely for validation purposes;
        // the real per-scope options are built inside the AddDbContext factory below.
        ValidateProvider(provider, connectionString);

        // Register AppDbContext using the factory-delegate overload of AddDbContext.
        // The factory captures both `provider` and `connectionString` from the outer
        // scope (both are read-only configuration values; capturing them in a closure
        // is safe because they never change after host startup).
        //
        // DatabaseProviderFactory.Configure is called once per scope (per HTTP request)
        // to build DbContextOptions; EF Core's model cache amortises the compilation cost.
        services.AddDbContext<AppDbContext>((serviceProvider, dbContextOptions) =>
            DatabaseProviderFactory.Configure(dbContextOptions, provider, connectionString));

        // Replace the context registration produced by AddDbContext with one that
        // supplies the `databaseProvider` string to AppDbContext's constructor.
        // AddDbContext registers a Scoped factory that constructs AppDbContext with
        // only DbContextOptions<AppDbContext>, which is insufficient for our two-arg
        // constructor. The explicit Scoped registration below wraps the EF-managed
        // DbContextOptions and passes the `provider` string as the second argument.
        //
        // ASSUMPTION: Replacing the last Scoped descriptor for AppDbContext is safe
        // because AddDbContext registers exactly one Scoped descriptor for the context type.
        // The RemoveAll + AddScoped pattern is the canonical resolution for this scenario.
        services.Remove(services.Last(d =>
            d.ServiceType == typeof(AppDbContext) &&
            d.Lifetime == ServiceLifetime.Scoped));

        services.AddScoped<AppDbContext>(sp =>
        {
            var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
            return new AppDbContext(options, provider);
        });
    }

    /// <summary>
    /// Validates that <paramref name="provider"/> is a recognised value by attempting
    /// a dry-run of <see cref="DatabaseProviderFactory.Configure"/> against a throwaway
    /// options builder.  Throws <see cref="InvalidOperationException"/> at startup if
    /// the provider name is unrecognised, ensuring a fast and clear failure.
    /// </summary>
    private static void ValidateProvider(string provider, string connectionString)
    {
        // A throwaway options builder — we only care whether Configure throws.
        var probe = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProviderFactory.Configure(probe, provider, connectionString);
    }
}
