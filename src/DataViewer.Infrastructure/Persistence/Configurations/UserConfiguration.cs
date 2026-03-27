using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataViewer.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent entity configuration for <see cref="User"/>.
/// </summary>
/// <remarks>
/// Key decisions:
/// <list type="bullet">
///   <item>
///     <description>
///       <c>UserName</c> and <c>Email</c> are capped at 256 characters — long enough
///       for any valid username / RFC 5321 address while remaining index-friendly on
///       both MySQL (767-byte index limit on utf8mb4) and PostgreSQL.
///     </description>
///   </item>
///   <item>
///     <description>
///       The unique index on <c>UserName</c> enforces uniqueness at the database level.
///       The application layer is responsible for normalising <c>UserName</c> to
///       lower-case before every read and write; this is the canonical enforcement
///       strategy for case-insensitive uniqueness across both PostgreSQL (where the
///       default collation is case-sensitive) and MySQL. The index therefore relies
///       on application-layer normalisation rather than a database collation annotation,
///       which would require provider-specific configuration.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>Role</c> is stored as its underlying <c>int</c> value. Enum member integers
///       are stable (never reordered) as documented on <see cref="UserRole"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Navigation collections that should not be eagerly loaded are explicitly
///       configured with <c>AutoInclude(false)</c> to prevent accidental hydration
///       via lazy-loading proxies (see User entity remarks and ADR-003).
///     </description>
///   </item>
///   <item>
///     <description>
///       This file owns the relationship configuration for both
///       <c>User → RefreshToken</c> and <c>User → UserPreference</c> (principal side).
///       The dependent-side configurations (<see cref="RefreshTokenConfiguration"/> and
///       <see cref="UserPreferenceConfiguration"/>) do NOT redeclare these relationships
///       to avoid duplicate registration and last-writer-wins fragility.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("Users");

        // ── Primary key ──────────────────────────────────────────────────────
        builder.HasKey(u => u.Id);

        // ── Properties ───────────────────────────────────────────────────────
        builder.Property(u => u.Id)
            .IsRequired()
            .ValueGeneratedNever(); // Application layer assigns Guid.NewGuid()

        builder.Property(u => u.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        // Store enum as its stable integer value (never reorder UserRole members).
        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(u => u.IsLocked)
            .IsRequired();

        builder.Property(u => u.LockoutUntil)
            .IsRequired(false);

        builder.Property(u => u.FailedLoginCount)
            .IsRequired();

        // UTC timestamps — normalised via AppDbContext.EnforceUtcDateTimes()
        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.LastLoginAt)
            .IsRequired(false);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique index on UserName enforces the uniqueness constraint at the DB level.
        // The application layer MUST normalise UserName to lower-case before every read
        // and write, making this index effectively case-insensitive without requiring
        // a provider-specific collation annotation.
        builder.HasIndex(u => u.UserName)
            .IsUnique()
            .HasDatabaseName("idx_user_username");

        // Non-unique index on Email to support alternative-login lookups.
        builder.HasIndex(u => u.Email)
            .HasDatabaseName("idx_user_email");

        // ── Relationships ────────────────────────────────────────────────────
        // This file (principal side) owns both relationship declarations.
        // RefreshTokenConfiguration and UserPreferenceConfiguration do NOT redeclare
        // these relationships — a duplicate HasOne/HasMany from both sides causes
        // last-writer-wins fragility when the ApplyConfiguration call order changes.

        // One User → many RefreshTokens (cascade delete).
        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Prevent accidental eager-loading of the unbounded RefreshTokens collection
        // (see User entity remarks and ADR-003 for the full rationale).
        builder.Navigation(u => u.RefreshTokens)
            .AutoInclude(false);

        // One User → zero or one UserPreference (shared-PK pattern, cascade delete).
        builder.HasOne(u => u.Preference)
            .WithOne(up => up.User)
            .HasForeignKey<UserPreference>(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.Preference)
            .AutoInclude(false);
    }
}
