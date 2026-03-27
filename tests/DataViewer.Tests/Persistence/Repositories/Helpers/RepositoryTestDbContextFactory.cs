using DataViewer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Creates and manages lightweight SQLite in-memory <see cref="AppDbContext"/>
/// instances for <see cref="DataViewer.Infrastructure.Persistence.Repositories.SystemSettingsRepository"/>
/// and <see cref="DataViewer.Infrastructure.Persistence.Repositories.UserPreferencesRepository"/>
/// integration tests.
/// </summary>
/// <remarks>
/// <para>
/// SQLite is preferred over the EF Core in-memory provider because it supports a
/// real relational schema (PKs, FKs, unique constraints) that exercises the same
/// code paths as the production MySQL / PostgreSQL providers.
/// </para>
/// <para>
/// The returned <see cref="SqliteConnection"/> MUST remain open for the entire test.
/// SQLite in-memory databases are destroyed the moment the last connection is closed.
/// Tests must dispose both the context and the connection in their cleanup phase.
/// </para>
/// </remarks>
public static class RepositoryTestDbContextFactory
{
    /// <summary>
    /// Creates a single, isolated SQLite in-memory <see cref="AppDbContext"/>
    /// with the schema applied. Suitable for tests that only need one context.
    /// </summary>
    public static async Task<(AppDbContext context, SqliteConnection connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var context = BuildContext(connection);
        await context.Database.EnsureCreatedAsync();

        return (context, connection);
    }

    /// <summary>
    /// Creates two <see cref="AppDbContext"/> instances that share the same
    /// underlying SQLite in-memory database. Useful when you want one context
    /// to act as the repository's context and a second independent context to
    /// verify the persisted state without the change-tracker masking the results.
    /// </summary>
    public static async Task<(
        AppDbContext primaryContext,
        AppDbContext verifyContext,
        SqliteConnection connection)> CreatePairAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var primary = BuildContext(connection);
        await primary.Database.EnsureCreatedAsync();

        // Second context on the same connection → same database.
        var verify = BuildContext(connection);

        return (primary, verify, connection);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AppDbContext BuildContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        // "mysql" suppresses the "jsonb"/"bytea" PostgreSQL-specific column type
        // annotations that SQLite does not understand at schema-creation time.
        // Column-type annotations are cosmetic for testing purposes; the tests
        // exercise repository behaviour, not DDL.
        return new AppDbContext(options, "mysql");
    }
}
