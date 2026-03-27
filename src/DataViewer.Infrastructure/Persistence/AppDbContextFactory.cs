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
/// This factory is intentionally wired to PostgreSQL because:
/// <list type="bullet">
///   <item>
///     <description>
///       PostgreSQL is the primary development database (see appsettings.Development.json).
///     </description>
///   </item>
///   <item>
///     <description>
///       Migrations generated against PostgreSQL use the correct provider-specific
///       annotations (<c>bytea</c>, <c>jsonb</c>, partial index filters with
///       double-quoted identifiers) that match the production configuration.
///     </description>
///   </item>
///   <item>
///     <description>
///       MySQL-specific migration scripts should be generated separately in a
///       MySQL-targeted migration project or by overriding the
///       <c>DATAVIEWER_DB_PROVIDER</c> environment variable before running
///       <c>dotnet ef migrations add</c>.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// The connection string targets a local development PostgreSQL instance.
/// Override it with the <c>DATAVIEWER_DESIGN_TIME_CONNECTION</c> environment
/// variable for CI environments.
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
        // Allow CI/CD environments to override the design-time connection string
        // without modifying this file.
        var connectionString =
            Environment.GetEnvironmentVariable("DATAVIEWER_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=dataviewer_dev;Username=dataviewer;Password=dataviewer";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                // No retry on failure during migrations — a transient failure mid-migration
                // should surface immediately rather than being silently retried.
            })
            .Options;

        // "postgresql" matches the provider string used by entity configurations so
        // that design-time migrations emit the correct provider-specific annotations
        // (bytea, jsonb, double-quoted HasFilter expressions).
        return new AppDbContext(options, "postgresql");
    }
}
