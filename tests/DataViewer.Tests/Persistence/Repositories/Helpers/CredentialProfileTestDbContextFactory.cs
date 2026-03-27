using DataViewer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Creates and manages a lightweight SQLite in-memory <see cref="AppDbContext"/>
/// for repository integration tests.
/// </summary>
/// <remarks>
/// <para>
/// SQLite is used instead of the EF Core in-memory provider because
/// <c>ExecuteUpdateAsync</c> (used by <c>SoftDeleteAsync</c> and
/// <c>ActivateAsync</c>) requires a real relational provider — it is not
/// supported by the non-relational in-memory provider.
/// </para>
/// <para>
/// Each test that calls <see cref="CreateAsync"/> gets an isolated, freshly-
/// migrated schema on a separate SQLite connection so that tests are fully
/// independent and can run in any order.
/// </para>
/// </remarks>
public static class CredentialProfileTestDbContextFactory
{
    /// <summary>
    /// Creates a new, isolated SQLite in-memory <see cref="AppDbContext"/> and
    /// ensures the schema has been created.  The caller is responsible for
    /// disposing both the context and the connection.
    /// </summary>
    public static async Task<(AppDbContext context, SqliteConnection connection)> CreateAsync()
    {
        // Keep connection open for the lifetime of the test — SQLite in-memory
        // databases are destroyed as soon as the last connection closes.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            // Suppress the ExecuteUpdate/ExecuteDelete transaction warning that
            // SQLite emits when these bulk operations run outside an explicit
            // transaction started by the test.  The repository opens its own
            // transactions internally for ActivateAsync.
            .ConfigureWarnings(w =>
                w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId
                    .AmbientTransactionWarning))
            .Options;

        // "mysql" is passed as the provider string so that
        // CredentialProfileConfiguration does not emit "bytea" (PostgreSQL-only),
        // which SQLite does not understand.  The column-type annotation is purely
        // cosmetic at the schema-creation level; the tests exercise behaviour, not
        // provider-specific DDL.
        var context = new AppDbContext(options, "mysql");

        await context.Database.EnsureCreatedAsync();

        return (context, connection);
    }
}
