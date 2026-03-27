using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataViewer.Infrastructure.Persistence;

/// <summary>
/// Configures the EF Core database provider on a <see cref="DbContextOptionsBuilder"/>
/// based on the <c>"DatabaseProvider"</c> application configuration key.
/// </summary>
/// <remarks>
/// <para>
/// Supported values for <c>"DatabaseProvider"</c> (case-insensitive):
/// <list type="table">
///   <listheader><term>Value</term><term>Provider</term><term>NuGet package</term></listheader>
///   <item><term>postgresql</term><term>Npgsql</term><term>Npgsql.EntityFrameworkCore.PostgreSQL (PostgreSQL License)</term></item>
///   <item><term>mysql</term><term>Pomelo</term><term>Pomelo.EntityFrameworkCore.MySql (MIT License)</term></item>
/// </list>
/// </para>
///
/// <para>
/// The method enforces strict provider validation: any value that is not exactly
/// <c>"postgresql"</c> or <c>"mysql"</c> (after normalisation) throws an
/// <see cref="InvalidOperationException"/> with a clear diagnostic message rather
/// than silently falling back to a default. A silent fallback on a typo such as
/// <c>"postgre"</c> or <c>"pgsql"</c> would produce a runtime failure with a
/// less actionable error message hours later during an actual database operation.
/// </para>
///
/// <para>
/// <strong>MigrationsAssembly routing:</strong>
/// Each provider routes its EF Core migrations to an isolated subfolder within
/// the Infrastructure assembly so that provider-specific migration history tables
/// and SQL scripts never cross-contaminate:
/// <list type="table">
///   <listheader><term>Provider</term><term>Migrations subfolder</term></listheader>
///   <item><term>postgresql</term><term><c>DataViewer.Infrastructure.Migrations.Postgresql</c></term></item>
///   <item><term>mysql</term><term><c>DataViewer.Infrastructure.Migrations.MySql</c></term></item>
/// </list>
/// The assembly name for both is <c>"DataViewer.Infrastructure"</c>; EF Core uses
/// the namespace to locate the migration type and history table independently.
/// </para>
///
/// <para>
/// <strong>MySQL ServerVersion pinning:</strong>
/// <see cref="ServerVersion.AutoDetect(string)"/> opens a live database connection during
/// DI container construction, which fails when the database is unavailable at startup
/// (e.g. Docker Compose race conditions). A pinned <see cref="MySqlServerVersion"/>
/// targeting MySQL 8.0.13 — the minimum version that supports functional/partial
/// index expressions — eliminates this dependency and ensures cold-start reliability.
/// </para>
///
/// <para>
/// <strong>Retry policy:</strong>
/// Both providers are configured with a three-attempt retry policy with a 5-second
/// maximum delay, which handles transient network blips in containerised deployments
/// without masking legitimate persistent failures (maxRetryCount = 3 keeps the total
/// startup delay bounded at ≤ 15 seconds).
/// </para>
/// </remarks>
public static class DatabaseProviderFactory
{
    /// <summary>
    /// Namespace prefix shared by all provider-specific migration namespaces.
    /// Changing this value requires regenerating all migrations.
    /// </summary>
    private const string MigrationsNamespaceRoot = "DataViewer.Infrastructure.Migrations";

    /// <summary>
    /// Assembly that contains all migration classes for every provider.
    /// EF Core uses this together with the namespace to locate the correct
    /// migration history table and migration types.
    /// </summary>
    private const string MigrationsAssemblyName = "DataViewer.Infrastructure";

    // ── Minimum MySQL version ────────────────────────────────────────────────
    // MySQL 8.0.13 is the minimum version that supports:
    //   • Functional index expressions (required by idx_profile_name partial index)
    //   • Native JSON column type enforcement
    // Do NOT lower this version without auditing all index filter expressions.
    private static readonly MySqlServerVersion MinimumMySqlVersion =
        new(new Version(8, 0, 13));

    // ── Retry policy constants ───────────────────────────────────────────────
    private const int RetryMaxCount = 3;
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the <paramref name="providerName"/> configuration value and configures
    /// the appropriate EF Core database provider on <paramref name="optionsBuilder"/>.
    /// </summary>
    /// <param name="optionsBuilder">
    /// The <see cref="DbContextOptionsBuilder"/> to configure. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="providerName">
    /// The raw value of the <c>"DatabaseProvider"</c> configuration key.
    /// Compared case-insensitively after trimming whitespace.
    /// </param>
    /// <param name="connectionString">
    /// The connection string used to connect to the target database.
    /// Passed directly to the provider's <c>UseXxx()</c> extension method.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="optionsBuilder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="providerName"/> is not <c>"postgresql"</c> or
    /// <c>"mysql"</c> (case-insensitive). The exception message names the unrecognised
    /// value and lists the valid choices to aid rapid diagnosis.
    /// </exception>
    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        string providerName,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // Normalise: trim whitespace and convert to lower-case for case-insensitive
        // comparison. An empty or whitespace-only value is treated as unrecognised
        // and will reach the default branch below, producing a clear error.
        var normalisedProvider = (providerName ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        switch (normalisedProvider)
        {
            case "postgresql":
                ConfigurePostgresql(optionsBuilder, connectionString);
                break;

            case "mysql":
                ConfigureMySql(optionsBuilder, connectionString);
                break;

            default:
                // Throw with a message that names the bad value and lists all valid
                // choices so operators can diagnose a mis-typed appsettings entry
                // immediately without needing to consult documentation.
                throw new InvalidOperationException(
                    $"Unrecognised DatabaseProvider value: '{providerName}'. "
                    + "Valid values are 'postgresql' or 'mysql' (case-insensitive). "
                    + "Update the 'DatabaseProvider' key in appsettings.json or the "
                    + "corresponding environment variable override.");
        }
    }

    // ── Private provider configuration helpers ────────────────────────────────

    /// <summary>
    /// Configures the Npgsql (PostgreSQL) EF Core provider with retry policy
    /// and the PostgreSQL-specific migrations assembly namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>MigrationsAssembly</c> call tells EF Core to look for migration
    /// classes in <see cref="MigrationsAssemblyName"/> rather than the context's
    /// own assembly (which is the default). This is required because
    /// <see cref="AppDbContext"/> lives in the same assembly as the migrations,
    /// so EF Core would find both providers' migrations without the namespace
    /// scoping applied by <c>MigrationsHistoryTable</c> and the namespace filter.
    /// </para>
    /// <para>
    /// No retry is configured here for migrations (design-time usage via
    /// <see cref="AppDbContextFactory"/>), but <see cref="EnableRetryOnFailure"/>
    /// is included for runtime resilience.
    /// </para>
    /// </remarks>
    private static void ConfigurePostgresql(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        // Npgsql.EntityFrameworkCore.PostgreSQL — PostgreSQL License (BSD-like)
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                // Route all PostgreSQL migrations to the Postgresql subfolder namespace.
                // This prevents migration type-name collisions and keeps provider-specific
                // migration histories completely isolated.
                npgsqlOptions.MigrationsAssembly(MigrationsAssemblyName);
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    schema: null); // PostgreSQL uses the default public schema

                // Transient-fault retry: up to 3 attempts with a 5-second ceiling.
                // Handles brief network interruptions in containerised environments.
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: RetryMaxCount,
                    maxRetryDelay: RetryMaxDelay,
                    errorCodesToAdd: null);
            });
    }

    /// <summary>
    /// Configures the Pomelo (MySQL) EF Core provider with a pinned server version,
    /// retry policy, and the MySQL-specific migrations assembly namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server version is pinned to MySQL 8.0.13 rather than auto-detected.
    /// <see cref="ServerVersion.AutoDetect(string)"/> opens a live database connection
    /// synchronously during DI container construction — if the database is not yet
    /// available (common in Docker Compose), this causes an unrecoverable startup
    /// failure before the retry policy is even registered.
    /// </para>
    /// <para>
    /// MySQL 8.0.13 is the minimum version required for the functional partial index
    /// on <c>CredentialProfiles.Name</c>. Lowering this version would require
    /// removing the <c>HasFilter</c> from <c>CredentialProfileConfiguration</c>.
    /// </para>
    /// </remarks>
    private static void ConfigureMySql(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        // Pomelo.EntityFrameworkCore.MySql — MIT License
        optionsBuilder.UseMySql(
            connectionString,
            MinimumMySqlVersion,
            mysqlOptions =>
            {
                // Route all MySQL migrations to the MySql subfolder namespace.
                mysqlOptions.MigrationsAssembly(MigrationsAssemblyName);
                mysqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");

                // Transient-fault retry: up to 3 attempts with a 5-second ceiling.
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: RetryMaxCount,
                    maxRetryDelay: RetryMaxDelay,
                    errorNumbersToAdd: null);
            });
    }

    /// <summary>
    /// Returns the fully-qualified migrations namespace for the specified provider.
    /// Used by <c>dotnet ef migrations add --namespace</c> to place generated
    /// migration files in the correct subfolder.
    /// </summary>
    /// <param name="providerName">
    /// Normalised (lower-case, trimmed) provider name — <c>"postgresql"</c> or <c>"mysql"</c>.
    /// </param>
    /// <returns>
    /// The fully-qualified migrations namespace string, e.g.
    /// <c>"DataViewer.Infrastructure.Migrations.Postgresql"</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown for unrecognised provider names.
    /// </exception>
    public static string GetMigrationsNamespace(string providerName) =>
        providerName.Trim().ToLowerInvariant() switch
        {
            "postgresql" => $"{MigrationsNamespaceRoot}.Postgresql",
            "mysql"      => $"{MigrationsNamespaceRoot}.MySql",
            _            => throw new InvalidOperationException(
                                $"Unrecognised DatabaseProvider value: '{providerName}'. "
                                + "Valid values are 'postgresql' or 'mysql'.")
        };
}
