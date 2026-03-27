using DataViewer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataViewer.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent entity configuration for <see cref="RefreshToken"/>.
/// </summary>
/// <remarks>
/// Key decisions:
/// <list type="bullet">
///   <item>
///     <description>
///       Cascade delete from <c>User</c> ensures that all refresh tokens are removed
///       when their owning user is deleted, preventing orphaned token rows.
///       The relationship itself is declared on the principal side in
///       <see cref="UserConfiguration"/> — this file does not redeclare it to avoid
///       duplicate registration and last-writer-wins fragility.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>TokenHash</c> is the only queryable lookup column; a unique index prevents
///       a SHA-256 collision from accidentally matching two tokens.
///     </description>
///   </item>
///   <item>
///     <description>
///       The <c>idx_refresh_user</c> index on <c>UserId</c> speeds up the common
///       query pattern of listing or invalidating all tokens for a given user.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("RefreshTokens");

        // ── Primary key ──────────────────────────────────────────────────────
        builder.HasKey(rt => rt.Id);

        // ── Properties ───────────────────────────────────────────────────────
        builder.Property(rt => rt.Id)
            .IsRequired()
            .ValueGeneratedNever(); // Application layer assigns Guid.NewGuid()

        builder.Property(rt => rt.UserId)
            .IsRequired();

        // SHA-256 hex string is exactly 64 characters; fixed length improves index density.
        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        // UTC timestamps — normalised via AppDbContext.EnforceUtcDateTimes()
        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.IsRevoked)
            .IsRequired();

        builder.Property(rt => rt.CreatedAt)
            .IsRequired();

        builder.Property(rt => rt.RevokedAt)
            .IsRequired(false);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique index on TokenHash — the primary lookup path for token exchange.
        // Also prevents accidental collision matching on a theoretical SHA-256 collision.
        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("idx_refresh_token_hash");

        // Named index required by acceptance criteria: idx_refresh_user.
        // Supports efficient bulk-revocation queries (e.g. "revoke all tokens for user X").
        builder.HasIndex(rt => rt.UserId)
            .HasDatabaseName("idx_refresh_user");

        // ── Relationships ────────────────────────────────────────────────────
        // Relationship ownership: see UserConfiguration.cs (principal side).
        // No HasOne/WithMany is declared here to avoid duplicate registration.
        // EF Core's last-writer-wins model resolution means a second call to configure
        // the same relationship from the dependent side would silently replace the
        // principal-side configuration, creating invisible ordering fragility.
    }
}
