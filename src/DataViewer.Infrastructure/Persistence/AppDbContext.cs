using DataViewer.Domain.Entities;
using DataViewer.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace DataViewer.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core database context for DataViewer.
/// Centralises all DbSet declarations, entity configuration registration,
/// and cross-cutting UTC DateTime enforcement via SaveChanges overrides.
/// </summary>
/// <remarks>
/// Supports both PostgreSQL (Npgsql) and MySQL (Pomelo) providers.
/// Provider selection is driven by the "DatabaseProvider" appsettings key —
/// no code changes are required to switch databases (Product Spec G-05).
///
/// <para>
/// The <see cref="_databaseProvider"/> field is injected at construction time
/// so that entity configurations can emit provider-specific column type annotations
/// (e.g. <c>jsonb</c> on PostgreSQL vs <c>JSON</c> on MySQL) without branching
/// inside the context itself.
/// </para>
///
/// <para>
/// All four <c>SaveChanges</c> overloads delegate to <see cref="EnforceUtcDateTimes"/>
/// before persisting so that no caller — including EF Core internal paths — can
/// bypass UTC normalisation.
/// </para>
/// </remarks>
public sealed class AppDbContext : DbContext
{
    private readonly string _databaseProvider;

    /// <summary>
    /// Initialises a new <see cref="AppDbContext"/> with the supplied options and
    /// database provider name.
    /// </summary>
    /// <param name="options">EF Core context options (connection string, provider, etc.).</param>
    /// <param name="databaseProvider">
    /// Lowercase provider identifier — either <c>"postgresql"</c> or <c>"mysql"</c>.
    /// Used by entity configurations to emit provider-specific column type annotations.
    /// </param>
    public AppDbContext(DbContextOptions<AppDbContext> options, string databaseProvider)
        : base(options)
    {
        _databaseProvider = databaseProvider;
    }

    // ── DbSets ───────────────────────────────────────────────────────────────
    // Using auto-properties (populated by EF Core during context construction)
    // rather than expression-bodied members (which call Set<T>() on every access).
    // This matches EF Core scaffolding conventions and avoids the per-access dispatch overhead.

    /// <summary>Application user accounts.</summary>
    public DbSet<User> Users { get; set; } = null!;

    /// <summary>Issued refresh tokens (active and revoked).</summary>
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

    /// <summary>Named AWS S3 credential profiles.</summary>
    public DbSet<CredentialProfile> CredentialProfiles { get; set; } = null!;

    /// <summary>Append-only audit log of every data-access and administration operation.</summary>
    public DbSet<AuditLogEntry> AuditLogEntries { get; set; } = null!;

    /// <summary>Per-user UI preferences.</summary>
    public DbSet<UserPreference> UserPreferences { get; set; } = null!;

    /// <summary>Singleton system-wide configuration row (always Id = 1).</summary>
    public DbSet<SystemSettings> SystemSettings { get; set; } = null!;

    // ── Model building ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> implementations.
        // Pass the provider string into configurations that need provider-specific
        // column type annotations (e.g. jsonb vs JSON, bytea vs longblob).
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialProfileConfiguration(_databaseProvider));
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration(_databaseProvider));
        modelBuilder.ApplyConfiguration(new UserPreferenceConfiguration());
        modelBuilder.ApplyConfiguration(new SystemSettingsConfiguration());
    }

    // ── UTC DateTime enforcement ─────────────────────────────────────────────
    // All four SaveChanges overloads are overridden to guarantee that no caller
    // (including EF Core internal paths that call the zero-argument forms) can
    // bypass UTC normalisation.

    /// <inheritdoc/>
    /// <remarks>
    /// Normalises <see cref="DateTimeKind.Unspecified"/> values to UTC before
    /// delegating to the base implementation. See <see cref="EnforceUtcDateTimes"/>.
    /// </remarks>
    public override int SaveChanges()
    {
        EnforceUtcDateTimes();
        return base.SaveChanges();
    }

    /// <inheritdoc/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceUtcDateTimes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        EnforceUtcDateTimes();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceUtcDateTimes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Iterates all <see cref="EntityState.Added"/> and <see cref="EntityState.Modified"/>
    /// tracked entries and normalises every <see cref="DateTime"/> property that carries
    /// <see cref="DateTimeKind.Unspecified"/> to <see cref="DateTimeKind.Utc"/> via
    /// <see cref="DateTime.SpecifyKind"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PostgreSQL's Npgsql provider rejects <c>DateTime</c> values with
    /// <see cref="DateTimeKind.Unspecified"/> at the driver level — this normalisation
    /// prevents those driver-level exceptions and ensures consistent UTC semantics
    /// across both the PostgreSQL and MySQL providers.
    /// </para>
    /// <para>
    /// The method is intentionally scoped to <c>Added</c> and <c>Modified</c> entries
    /// only (skipping <c>Unchanged</c>, <c>Deleted</c>, and <c>Detached</c>) and
    /// filters properties to those whose CLR type is <see cref="DateTime"/> or
    /// <see cref="Nullable{T}"/> of <see cref="DateTime"/>. This keeps the O(E × P)
    /// iteration cost proportional to the actual write workload rather than the full
    /// tracked graph (PERF-1).
    /// </para>
    /// </remarks>
    private void EnforceUtcDateTimes()
    {
        foreach (var entry in ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties
                .Where(p =>
                    p.Metadata.ClrType == typeof(DateTime) ||
                    p.Metadata.ClrType == typeof(DateTime?)))
            {
                if (property.CurrentValue is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
                    property.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }
    }
}
