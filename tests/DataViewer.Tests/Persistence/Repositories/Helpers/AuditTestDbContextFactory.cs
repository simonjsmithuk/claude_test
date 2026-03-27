using DataViewer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Creates and manages a lightweight SQLite in-memory <see cref="AppDbContext"/>
/// for <see cref="DataViewer.Infrastructure.Persistence.Repositories.AuditRepository"/>
/// integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Two independent contexts can be created from the same underlying SQLite connection
/// so that the test can simulate the repository's dual-context design
/// (a factory-created write context + a shared read context backed by the same database).
/// </para>
/// <para>
/// The returned <see cref="SqliteConnection"/> must be kept open for the entire test
/// because SQLite in-memory databases are destroyed as soon as the last connection
/// closes.  Tests must dispose both contexts and the connection in their cleanup phase.
/// </para>
/// </remarks>
public static class AuditTestDbContextFactory
{
    // ── Public factory methods ────────────────────────────────────────────────

    /// <summary>
    /// Creates a single, isolated SQLite in-memory <see cref="AppDbContext"/> and
    /// ensures the schema has been created.  Suitable for unit tests that only need
    /// one context (e.g. testing the read path in isolation).
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
    /// Creates a pair of <see cref="AppDbContext"/> instances that share the same
    /// underlying SQLite in-memory database.  The first context acts as the
    /// "read context" (analogous to the request-scoped context injected into the
    /// repository); the second acts as the "write context" (analogous to the
    /// factory-created context used for audit INSERTs).
    /// </summary>
    /// <remarks>
    /// Both contexts are built from the same <paramref name="connection"/> so they
    /// share the same in-memory database.  Schema creation is performed on the read
    /// context only (once is sufficient).
    /// </remarks>
    public static async Task<(
        AppDbContext readContext,
        AppDbContext writeContext,
        SqliteConnection connection)> CreatePairAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var readContext = BuildContext(connection);
        await readContext.Database.EnsureCreatedAsync();

        // Second context shares the same connection — same in-memory database.
        var writeContext = BuildContext(connection);

        return (readContext, writeContext, connection);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AppDbContext BuildContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        // "mysql" is used as the provider string so that the AuditLogEntryConfiguration
        // does not emit "jsonb" (a PostgreSQL-specific type that SQLite does not
        // understand at schema-creation time). The column type annotation is purely
        // cosmetic for in-memory testing; tests exercise behaviour, not DDL.
        return new AppDbContext(options, "mysql");
    }
}
