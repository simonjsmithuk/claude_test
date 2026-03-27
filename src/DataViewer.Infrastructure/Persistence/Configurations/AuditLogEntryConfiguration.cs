using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataViewer.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent entity configuration for <see cref="AuditLogEntry"/>.
/// </summary>
/// <remarks>
/// Key decisions:
/// <list type="bullet">
///   <item>
///     <description>
///       The <c>Parameters</c> column type is provider-specific: <c>jsonb</c> on
///       PostgreSQL (binary JSON with GIN indexing support) and <c>JSON</c> on MySQL
///       (validated text JSON). The provider name is passed in at construction time
///       to emit the correct annotation without any runtime branching in the context.
///     </description>
///   </item>
///   <item>
///     <description>
///       A composite index on <c>(UserId, TimestampUtc DESC)</c> supports the most
///       common audit log query: "show me all actions for user X, newest first".
///     </description>
///   </item>
///   <item>
///     <description>
///       A second composite index on <c>(ActionType, TimestampUtc DESC)</c> supports
///       admin queries filtered by operation category (e.g. "show all Login failures
///       in the last 24 hours").
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>AuditLogEntry</c> uses private setters and a private constructor; EF Core
///       accesses them via reflection. The entity is append-only — no navigation
///       property to <see cref="User"/> is declared (ADR-004).
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    // Lowercase provider identifier: "postgresql" or "mysql"
    private readonly string _databaseProvider;

    /// <summary>
    /// Initialises the configuration with the active database provider name so that
    /// the <c>Parameters</c> column can receive the correct provider-specific type annotation.
    /// </summary>
    /// <param name="databaseProvider">
    /// <c>"postgresql"</c> → column type <c>jsonb</c>; any other value → column type <c>JSON</c>.
    /// </param>
    public AuditLogEntryConfiguration(string databaseProvider)
    {
        _databaseProvider = databaseProvider;
    }

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("AuditLogEntries");

        // ── Primary key ──────────────────────────────────────────────────────
        builder.HasKey(a => a.Id);

        // ── Properties ───────────────────────────────────────────────────────
        builder.Property(a => a.Id)
            .IsRequired()
            .ValueGeneratedNever(); // Application layer assigns Guid.NewGuid() via factory methods

        // UserId is stored as a plain FK value — no navigation property (ADR-004).
        // Guid.Empty is the sentinel for system-initiated actions.
        builder.Property(a => a.UserId)
            .IsRequired();

        // Store enum as its stable integer value (never reorder AuditActionType members).
        builder.Property(a => a.ActionType)
            .IsRequired()
            .HasConversion<int>();

        // UTC timestamp — normalised via AppDbContext.EnforceUtcDateTimes()
        builder.Property(a => a.TimestampUtc)
            .IsRequired();

        builder.Property(a => a.IpAddress)
            .IsRequired(false)
            .HasMaxLength(45); // IPv6 max length is 39 chars; 45 provides padding

        // Provider-specific JSON column type annotation (acceptance criteria):
        //   PostgreSQL  → jsonb  (binary JSON; supports GIN indexing and efficient operators)
        //   MySQL       → JSON   (validated text JSON; enforced by the MySQL engine)
        var parametersColumnType = IsPostgres() ? "jsonb" : "JSON";
        builder.Property(a => a.Parameters)
            .IsRequired(false)
            .HasColumnType(parametersColumnType);

        builder.Property(a => a.ResultCount)
            .IsRequired(false);

        builder.Property(a => a.S3ObjectKey)
            .IsRequired(false)
            .HasMaxLength(1024);

        builder.Property(a => a.ProfileName)
            .IsRequired(false)
            .HasMaxLength(256);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Composite index: (UserId ASC, TimestampUtc DESC)
        // Primary access pattern: "show all actions for user X, newest first".
        builder.HasIndex(a => new { a.UserId, a.TimestampUtc })
            .HasDatabaseName("idx_audit_user_timestamp")
            .IsDescending(false, true); // UserId ASC, TimestampUtc DESC

        // Composite index: (ActionType ASC, TimestampUtc DESC)
        // Secondary access pattern: "show all events of type Y, newest first".
        builder.HasIndex(a => new { a.ActionType, a.TimestampUtc })
            .HasDatabaseName("idx_audit_actiontype_timestamp")
            .IsDescending(false, true); // ActionType ASC, TimestampUtc DESC
    }

    /// <summary>
    /// Returns <see langword="true"/> when the active database provider is PostgreSQL.
    /// </summary>
    private bool IsPostgres() =>
        _databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase);
}
