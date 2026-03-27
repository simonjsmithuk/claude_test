using DataViewer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataViewer.Tests.Auth.Helpers;

/// <summary>
/// Creates and manages lightweight SQLite in-memory <see cref="AppDbContext"/>
/// instances for <see cref="DataViewer.Infrastructure.Auth.RefreshTokenRepository"/>
/// integration tests.
/// </summary>
/// <remarks>
/// SQLite is used (rather than the EF Core in-memory provider) because
/// <c>ExecuteUpdateAsync</c> — used in the repository's atomic validate-and-revoke
/// and bulk-revoke operations — is not supported by the in-memory provider.
/// </remarks>
internal static class RefreshTokenTestDbContextFactory
{
    // ── Public factory methods ────────────────────────────────────────────────

    /// <summary>
    /// Creates a single <see cref="AppDbContext"/> on an in-memory SQLite database
    /// with the full schema applied. Suitable for single-context tests.
    /// </summary>
    internal static async Task<(AppDbContext context, SqliteConnection connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var context = BuildContext(connection);
        await context.Database.EnsureCreatedAsync();

        return (context, connection);
    }

    /// <summary>
    /// Creates a primary/verify pair of <see cref="AppDbContext"/> instances on the
    /// same underlying SQLite in-memory database. Use the primary context for writes
    /// (the repository under test) and the verify context to read back data without
    /// the change-tracker masking the results.
    /// </summary>
    internal static async Task<(
        AppDbContext primaryContext,
        AppDbContext verifyContext,
        SqliteConnection connection)> CreatePairAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var primary = BuildContext(connection);
        await primary.Database.EnsureCreatedAsync();

        // Second context shares the same connection — same in-memory database.
        var verify = BuildContext(connection);

        return (primary, verify, connection);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AppDbContext BuildContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        // "mysql" suppresses PostgreSQL-specific column type annotations
        // that SQLite does not understand at schema-creation time.
        return new AppDbContext(options, "mysql");
    }
}
