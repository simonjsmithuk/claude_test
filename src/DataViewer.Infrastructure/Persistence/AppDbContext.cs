using DataViewer.Domain.Entities;
using DataViewer.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace DataViewer.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core database context for DataViewer.
/// Centralises all DbSet declarations, entity configuration registration,
/// and cross-cutting UTC DateTime enforcement via a SaveChanges interceptor.
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

    /// <summary>Application user accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Issued refresh tokens (active and revoked).</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Named AWS S3 credential profiles.</summary>
    public DbSet<CredentialProfile> CredentialProfiles => Set<CredentialProfile>();

    /// <summary>Append-only audit log of every data-access and administration operation.</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>Per-user UI preferences.</summary>
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    /// <summary>Singleton system-wide configuration row (always Id = 1).</summary>
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    // ── Model building ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> implementations.
        // Pass the provider string into configurations that need provider-specific
        // column type annotations (e.g. jsonb vs JSON for the Parameters column).
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialProfileConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration(_databaseProvider));
        modelBuilder.ApplyConfiguration(new UserPreferenceConfiguration());
        modelBuilder.ApplyConfiguration(new SystemSettingsConfiguration());
    }

    // ── UTC DateTime enforcement ─────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Normalises <see cref="DateTimeKind.Unspecified"/> values to UTC before
    /// delegating to the base implementation. See <see cref="EnforceUtcDateTimes"/>.
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceUtcDateTimes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
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
    /// Iterates all tracked entity entries and normalises every <see cref="DateTime"/>
    /// property that carries <see cref="DateTimeKind.Unspecified"/> to
    /// <see cref="DateTimeKind.Utc"/> via <see cref="DateTime.SpecifyKind"/>.
    /// </summary>
    /// <remarks>
    /// PostgreSQL's Npgsql provider rejects <c>DateTime</c> values with
    /// <see cref="DateTimeKind.Unspecified"/> at the driver level — this normalisation
    /// prevents those driver-level exceptions and ensures consistent UTC semantics
    /// across both the PostgreSQL and MySQL providers.
    /// </remarks>
    private void EnforceUtcDateTimes()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
                    property.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }
    }
}
