using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataViewer.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="AppDbContext"/>, required by the EF Core
/// tooling (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppDbContext"/> has a non-default constructor signature
/// <c>(DbContextOptions&lt;AppDbContext&gt;, string databaseProvider)</c>.
/// Without this factory, <c>dotnet ef</c> cannot instantiate the context at
/// design time and fails with:
/// <c>"Unable to create an object of type 'AppDbContext'."</c>
/// </para>
///
/// <para>
/// <strong>Provider selection at design time:</strong>
/// The provider is read from the <c>DATAVIEWER_DB_PROVIDER</c> environment variable
/// (defaulting to <c>"postgresql"</c> when absent). This allows the same factory
/// to serve both PostgreSQL and MySQL migration workflows without code changes:
/// <code>
/// # PostgreSQL migrations (default):
/// dotnet ef migrations add InitialCreate \
///     --project src/DataViewer.Infrastructure \
///     --startup-project src/DataViewer.API \
///     --namespace DataViewer.Infrastructure.Migrations.Postgresql
///
/// # MySQL migrations:
/// DATAVIEWER_DB_PROVIDER=mysql \
/// DATAVIEWER_DESIGN_TIME_CONNECTION="Server=localhost;Port=3306;Database=dataviewer_dev;User=dataviewer;Password=dataviewer" \
/// dotnet ef migrations add InitialCreate \
///     --project src/DataViewer.Infrastructure \
///     --startup-project src/DataViewer.API \
///     --namespace DataViewer.Infrastructure.Migrations.MySql
/// </code>
/// </para>
///
/// <para>
/// <strong>MigrationsAssembly routing:</strong>
/// Provider selection is delegated to <see cref="DatabaseProviderFactory.Configure"/>,
/// which registers the correct <c>MigrationsAssembly</c> and <c>MigrationsHistoryTable</c>
/// for each provider. This guarantees that <c>dotnet ef</c> tooling finds the
/// generated migration files in the correct provider-specific namespace subfolder.
/// </para>
///
/// <para>
/// This class is only compiled into the Infrastructure assembly and is never
/// instantiated at runtime — EF Core tooling discovers it via the
/// <see cref="IDesignTimeDbContextFactory{TContext}"/> interface at design time only.
/// </para>
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    public AppDbContext CreateDbContext(string[] args)
    {
        // Read the provider from the environment so that the same factory handles
        // both PostgreSQL (default) and MySQL migration generation workflows.
        // See the class-level remarks for the full usage examples.
        var provider = (
            Environment.GetEnvironmentVariable("DATAVIEWER_DB_PROVIDER")
            ?? "postgresql")
            .Trim()
            .ToLowerInvariant();

        // Allow CI/CD environments to override the design-time connection string
        // without modifying this file.
        var connectionString =
            Environment.GetEnvironmentVariable("DATAVIEWER_DESIGN_TIME_CONNECTION")
            ?? ResolveDefaultConnectionString(provider);

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Delegate provider wiring (UseNpgsql / UseMySql, MigrationsAssembly,
        // retry policy) entirely to DatabaseProviderFactory so there is a single
        // authoritative source for these decisions.
        // Note: no retry on failure during migrations — a transient failure
        // mid-migration should surface immediately rather than being silently retried.
        DatabaseProviderFactory.Configure(optionsBuilder, provider, connectionString);

        // "provider" is passed to AppDbContext so entity configurations emit the
        // correct provider-specific column type annotations (bytea/jsonb for PostgreSQL,
        // longblob/JSON for MySQL) in the generated migration Up() code.
        return new AppDbContext(optionsBuilder.Options, provider);
    }

    /// <summary>
    /// Returns the default local development connection string for <paramref name="provider"/>
    /// when <c>DATAVIEWER_DESIGN_TIME_CONNECTION</c> is not set.
    /// </summary>
    /// <param name="provider">Normalised (lower-case) provider name.</param>
    /// <returns>A connection string for a local database instance.</returns>
    private static string ResolveDefaultConnectionString(string provider) =>
        provider switch
        {
            "mysql"      => "Server=localhost;Port=3306;Database=dataviewer_dev;User=dataviewer;Password=dataviewer",
            _            => "Host=localhost;Port=5432;Database=dataviewer_dev;Username=dataviewer;Password=dataviewer"
        };
}
