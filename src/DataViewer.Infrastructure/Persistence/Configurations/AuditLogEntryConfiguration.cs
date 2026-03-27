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
///       (validated text JSON). The provider name is resolved at construction time
///       to a validated column type string, eliminating silent fallback on typos.
///     </description>
///   </item>
///   <item>
///     <description>
///       A composite index on <c>(UserId ASC, TimestampUtc DESC)</c> supports the most
///       common audit log query: "show me all actions for user X, newest first".
///     </description>
///   </item>
///   <item>
///     <description>
///       A second composite index on <c>(ActionType ASC, TimestampUtc DESC)</c> supports
///       admin queries filtered by operation category (e.g. "show all Login failures
///       in the last 24 hours").
///     </description>
///   </item>
///   <item>
///     <description>
///       A standalone index on <c>TimestampUtc DESC</c> supports time-range-only
///       dashboard queries (e.g. "all events in the last hour") that have no leading
///       equality filter on UserId or ActionType. Without this index such queries
///       would produce full table scans on a large audit log.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>AuditLogEntry</c> uses private setters and a private constructor; EF Core
///       accesses them via reflection in default (non-compiled-model) mode.
///       No <c>UsePropertyAccessMode</c> override is required for standard reflection-based
///       materialisation — EF Core 8 resolves private constructors automatically.
///       The entity is append-only — no navigation property to <see cref="User"/>
///       is declared (ADR-004).
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    // Resolved once at construction time; never re-evaluated per Configure() call.
    private readonly string _parametersColumnType;

    /// <summary>
    /// Initialises the configuration with the active database provider name so that
    /// the <c>Parameters</c> column receives the correct provider-specific type annotation.
    /// </summary>
    /// <param name="databaseProvider">
    /// Lowercase provider identifier — <c>"postgresql"</c> or <c>"mysql"</c>.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="databaseProvider"/> is not a recognised value.
    /// An unrecognised provider (e.g. a typo such as <c>"postgre"</c>) would silently
    /// produce an incorrect column type annotation, which is worse than a fast-fail.
    /// </exception>
    public AuditLogEntryConfiguration(string databaseProvider)
    {
        _parametersColumnType = ResolveParametersColumnType(databaseProvider);
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
        //   PostgreSQL → jsonb (binary JSON; supports GIN indexing and efficient operators)
        //   MySQL      → JSON  (validated text JSON; enforced by the MySQL engine)
        builder.Property(a => a.Parameters)
            .IsRequired(false)
            .HasColumnType(_parametersColumnType);

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

        // Standalone index: TimestampUtc DESC
        // Supports time-range-only dashboard queries (e.g. "all events in the last hour")
        // without requiring a leading equality filter on UserId or ActionType.
        // Without this index, range-only WHERE TimestampUtc > @cutoff queries would
        // produce full table scans on a large audit log (PERF-2).
        builder.HasIndex(a => a.TimestampUtc)
            .HasDatabaseName("idx_audit_timestamp")
            .IsDescending(true); // TimestampUtc DESC
    }

    /// <summary>
    /// Maps the database provider name to the correct JSON column type string.
    /// Throws <see cref="NotSupportedException"/> for unrecognised provider names
    /// rather than silently falling back to a potentially incorrect type.
    /// </summary>
    /// <param name="databaseProvider">
    /// Lowercase provider identifier — <c>"postgresql"</c> or <c>"mysql"</c>.
    /// </param>
    /// <returns>The EF Core column type string for the <c>Parameters</c> JSON column.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="databaseProvider"/> is not <c>"postgresql"</c>
    /// or <c>"mysql"</c> (case-insensitive). A silent fallback would mask typos
    /// such as <c>"postgre"</c> or <c>"pgsql"</c> and produce wrong column annotations.
    /// </exception>
    private static string ResolveParametersColumnType(string databaseProvider) =>
        databaseProvider.ToLowerInvariant() switch
        {
            "postgresql" => "jsonb",
            "mysql"      => "JSON",
            _            => throw new NotSupportedException(
                                $"Unsupported database provider: '{databaseProvider}'. " +
                                "Expected 'postgresql' or 'mysql'.")
        };
}
