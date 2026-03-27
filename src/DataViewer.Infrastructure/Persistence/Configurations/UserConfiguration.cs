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
///       The unique index on <c>UserName</c> uses a case-insensitive approach: the
///       application layer normalises to lower-case before persistence, so the index
///       itself does not need a special collation — normalisation is the enforcement.
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
        // The application layer normalises UserName to lower-case before persistence,
        // so a case-insensitive collation is not required — normalisation IS the guard.
        builder.HasIndex(u => u.UserName)
            .IsUnique()
            .HasDatabaseName("idx_user_username");

        // Non-unique index on Email to support alternative-login lookups.
        builder.HasIndex(u => u.Email)
            .HasDatabaseName("idx_user_email");

        // ── Relationships ────────────────────────────────────────────────────

        // One User → many RefreshTokens (cascade delete configured on RefreshTokenConfiguration).
        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Prevent accidental eager-loading of the unbounded RefreshTokens collection
        // (see User entity remarks and ADR-003 for the full rationale).
        builder.Navigation(u => u.RefreshTokens)
            .AutoInclude(false);

        // One User → zero or one UserPreference (shared-PK pattern).
        builder.HasOne(u => u.Preference)
            .WithOne(up => up.User)
            .HasForeignKey<UserPreference>(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.Preference)
            .AutoInclude(false);
    }
}
