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
    /// <strong>AuditRepository — Scoped with IDbContextFactory dependency:</strong>
    /// <see cref="AuditRepository"/> is Scoped (matching the lifetime of the injected
    /// request-scoped <see cref="AppDbContext"/> used for reads).  For writes it
    /// consumes <see cref="IDbContextFactory{TContext}"/>, which is Singleton by default
    /// when registered via <c>AddDbContextFactory</c>.  Resolving a Singleton factory
    /// from a Scoped service is safe because the factory itself is stateless.
    ///
    /// <strong>AppDbContext lifetime — Scoped (EF Core default):</strong>
    /// DbContext is inherently not thread-safe and is designed for a single unit-of-work
    /// per HTTP request. Scoped lifetime matches this design exactly.
    ///
    /// <strong>IDbContextFactory lifetime — Singleton (EF Core default):</strong>
    /// The factory registered by <c>AddDbContextFactory</c> is Singleton by default,
    /// but each <c>CreateDbContextAsync()</c> call returns a new, independent
    /// <see cref="AppDbContext"/> instance. This is the recommended pattern for services
    /// that need to open their own independent database connections (e.g. background
    /// services, audit repositories that must not share the request transaction).
    ///
    /// <strong>IMemoryCache lifetime — Singleton:</strong>
    /// <c>AddMemoryCache()</c> registers <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
    /// as a Singleton. This is the correct lifetime for an in-process cache — the cache
    /// must outlive individual HTTP requests so that entries populated in one request are
    /// available to subsequent requests.  <see cref="SystemSettingsRepository"/> and
    /// <see cref="UserPreferencesRepository"/> consume the Singleton
    /// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> from their
    /// Scoped constructors — consuming a Singleton from a Scoped service is safe.
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

        // ── In-process memory cache ───────────────────────────────────────────
        // AddMemoryCache() is idempotent — safe to call multiple times.
        // The Singleton IMemoryCache is consumed by SystemSettingsRepository and
        // UserPreferencesRepository (both Scoped) for 5-minute TTL caching of
        // their respective entities (Acceptance Criteria — TASK-013).
        services.AddMemoryCache();

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
    ///
    /// <para>
    /// <see cref="AuditRepository"/> is also Scoped.  Although its write path uses
    /// the Singleton <see cref="IDbContextFactory{TContext}"/> (which creates independent
    /// contexts on demand), its read path consumes the request-scoped
    /// <see cref="AppDbContext"/>, so Scoped is the correct lifetime.
    /// </para>
    ///
    /// <para>
    /// <see cref="SystemSettingsRepository"/> and <see cref="UserPreferencesRepository"/>
    /// are Scoped and consume the Singleton <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
    /// for 5-minute TTL caching.  Consuming a Singleton from a Scoped service is safe.
    /// </para>
    /// </remarks>
    private static void RegisterRepositories(IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICredentialProfileRepository, CredentialProfileRepository>();

        // AuditRepository is Scoped:
        //  • Write path: uses IDbContextFactory<AppDbContext> (Singleton) to create
        //    short-lived, independent contexts so audit INSERTs are committed
        //    independently of the request's ambient transaction (Acceptance Criteria).
        //  • Read path: uses the request-scoped AppDbContext injected via constructor.
        // Registering as Scoped (not Singleton) is required because the constructor
        // also takes the request-scoped AppDbContext for the read path.
        services.AddScoped<IAuditRepository, AuditRepository>();

        // SystemSettingsRepository — Scoped.
        // Queries/caches the singleton SystemSettings row (Id = 1).
        // Cache entries have a 5-minute absolute TTL; UpdateAsync explicitly evicts
        // and repopulates the cache on every successful write (Acceptance Criteria).
        services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();

        // UserPreferencesRepository — Scoped.
        // Upserts per-user UserPreference rows (shared-PK pattern with User).
        // Cache entries are keyed per-user with a 5-minute absolute TTL;
        // UpsertAsync explicitly evicts and repopulates the cache on every write
        // (Acceptance Criteria).
        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
    }

    /// <summary>
    /// Resolves the database provider and connection string from configuration and
    /// registers both <see cref="AppDbContext"/> (Scoped) and
    /// <see cref="IDbContextFactory{TContext}"/> (Singleton) using custom factories
    /// that forward the captured <c>databaseProvider</c> string to
    /// <see cref="AppDbContext"/>'s two-argument constructor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why both AddDbContext and AddDbContextFactory are registered:</strong>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>AddDbContext</c> registers a Scoped <see cref="AppDbContext"/> for the
    ///       standard request-scoped unit-of-work pattern used by all repositories that
    ///       participate in the request transaction.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>AddDbContextFactory</c> registers a Singleton
    ///       <see cref="IDbContextFactory{TContext}"/> that <see cref="AuditRepository"/>
    ///       uses to create short-lived, independent <see cref="AppDbContext"/> instances
    ///       for each audit INSERT. These contexts are committed and disposed immediately,
    ///       independently of the request's ambient transaction.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    ///
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
    /// Both the Scoped context and the factory descriptor are patched with a custom
    /// lambda that passes the captured <c>provider</c> string to the constructor.
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

        // ── Scoped DbContext (request unit-of-work) ───────────────────────────
        // Register AppDbContext using the factory-delegate overload of AddDbContext.
        // The factory captures both `provider` and `connectionString` from the outer
        // scope (both are read-only configuration values; capturing them in a closure
        // is safe because they never change after host startup).
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

        // ── Singleton IDbContextFactory (independent scope per CreateDbContextAsync) ──
        // AddDbContextFactory registers:
        //   • DbContextOptions<AppDbContext> as Singleton (built once; EF model cache reuse)
        //   • IDbContextFactory<AppDbContext> as Singleton
        // Each call to IDbContextFactory<AppDbContext>.CreateDbContextAsync() returns a NEW,
        // independently-owned AppDbContext instance — the factory itself is Singleton but
        // each produced context is transient and must be disposed by the caller (await using).
        //
        // This is the recommended EF Core pattern for services that need their own
        // independent database connection/transaction (e.g. AuditRepository.InsertAsync).
        // See: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory
        services.AddDbContextFactory<AppDbContext>((dbContextOptions) =>
            DatabaseProviderFactory.Configure(dbContextOptions, provider, connectionString),
            lifetime: ServiceLifetime.Singleton);

        // Patch the factory's internal descriptor so that each created context
        // receives the `provider` string — same pattern as the Scoped context patch above.
        // AddDbContextFactory registers an IDbContextFactory<AppDbContext> backed by
        // a DbContextOptions<AppDbContext> Singleton. We replace the factory descriptor
        // with one that uses our two-arg AppDbContext constructor.
        //
        // ASSUMPTION: AddDbContextFactory registers exactly one Singleton descriptor for
        // IDbContextFactory<AppDbContext>. Removing the last matching descriptor and
        // replacing it is safe for the same reason as the Scoped context patch.
        var factoryDescriptor = services.LastOrDefault(d =>
            d.ServiceType == typeof(IDbContextFactory<AppDbContext>) &&
            d.Lifetime == ServiceLifetime.Singleton);

        if (factoryDescriptor is not null)
        {
            services.Remove(factoryDescriptor);
        }

        // Register a custom Singleton factory implementation that creates AppDbContext
        // instances with both DbContextOptions<AppDbContext> and the provider string.
        // The PooledDbContextFactory alternative is not used here because audit writes
        // are relatively infrequent and context pooling would complicate the two-arg
        // constructor pattern without meaningful throughput benefit for this use case.
        services.AddSingleton<IDbContextFactory<AppDbContext>>(sp =>
        {
            var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
            // Capture `provider` from the outer closure — safe because it is a
            // read-only configuration value that does not change after host startup.
            return new AuditDbContextFactory(options, provider);
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
