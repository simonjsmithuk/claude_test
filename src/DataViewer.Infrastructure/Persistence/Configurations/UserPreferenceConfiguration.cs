using DataViewer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataViewer.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent entity configuration for <see cref="UserPreference"/>.
/// </summary>
/// <remarks>
/// Key decisions:
/// <list type="bullet">
///   <item>
///     <description>
///       <c>UserId</c> is simultaneously the primary key AND the foreign key to
///       <see cref="User"/> (shared primary key / table-per-hierarchy pattern).
///       This enforces the one-to-one cardinality at the database level with no
///       additional unique index required — there can only ever be one preference
///       row per user because the user's own PK is the row's PK.
///     </description>
///   </item>
///   <item>
///     <description>
///       The relationship's principal side is configured in <see cref="UserConfiguration"/>;
///       this configuration declares the dependent-side mirror so that the configuration
///       set is self-contained and symmetrical.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>PreferredProfileId</c> is an optional FK-by-value to
///       <see cref="CredentialProfile"/> — no navigation property is required because
///       the profile entity is not traversed from preferences in any current query.
///       By design — no navigation property. See ADR-004.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("UserPreferences");

        // ── Primary key / Foreign key (shared-PK pattern) ────────────────────
        // UserId is BOTH the PK and the FK to User.Id.
        // HasKey() declares the PK; the FK is declared in the relationship below.
        builder.HasKey(up => up.UserId);

        // ── Properties ───────────────────────────────────────────────────────
        builder.Property(up => up.UserId)
            .IsRequired()
            .ValueGeneratedNever(); // Value comes from the owning User.Id — never generated here

        builder.Property(up => up.DefaultPageSize)
            .IsRequired();

        builder.Property(up => up.DefaultDateRangeDays)
            .IsRequired();

        // Optional FK-by-value to CredentialProfile — no navigation property (ADR-004).
        builder.Property(up => up.PreferredProfileId)
            .IsRequired(false);

        // ── Relationships ────────────────────────────────────────────────────

        // UserId is the FK for the one-to-one relationship with User.
        // Cascade delete: removing the User also removes their preferences row.
        // The principal side is mirrored in UserConfiguration.
        builder.HasOne(up => up.User)
            .WithOne(u => u.Preference)
            .HasForeignKey<UserPreference>(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
