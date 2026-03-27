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
///       <see cref="User"/> (shared primary key / one-to-one pattern).
///       This enforces the one-to-one cardinality at the database level with no
///       additional unique index required — there can only ever be one preference
///       row per user because the user's own PK is the row's PK.
///     </description>
///   </item>
///   <item>
///     <description>
///       Relationship ownership: the <c>User → UserPreference</c> relationship is
///       declared on the principal side in <see cref="UserConfiguration"/>.
///       This file does NOT redeclare <c>HasOne/WithOne</c> to avoid duplicate
///       registration. EF Core's last-writer-wins model resolution means a second
///       call to configure the same relationship would silently replace the
///       principal-side configuration, creating invisible ordering fragility.
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
        // HasKey() declares the PK; the FK relationship is declared in UserConfiguration
        // (principal side) and must NOT be redeclared here.
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
        // Relationship ownership: see UserConfiguration.cs (principal side).
        // No HasOne/WithOne is declared here to avoid duplicate registration.
    }
}
