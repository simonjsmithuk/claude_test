using DataViewer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataViewer.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent entity configuration for <see cref="CredentialProfile"/>.
/// </summary>
/// <remarks>
/// Key decisions:
/// <list type="bullet">
///   <item>
///     <description>
///       <c>EncryptedSecretKey</c> is stored as a <c>byte[]</c> (<c>varbinary</c> /
///       <c>bytea</c> column). The byte layout is
///       <c>[16-byte AES IV][ciphertext]</c> — see <see cref="CredentialProfile.IvSizeBytes"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       The unique partial index <c>idx_profile_name</c> covers only rows where
///       <c>IsDeleted = false</c>. This allows a new profile with the same name to be
///       created after the original is soft-deleted, while still preventing duplicate
///       active names (Product Spec G-04 / soft-delete design).
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>AccessKeyId</c> is capped at 128 characters — more than sufficient for the
///       AWS IAM 20-character key ID, with headroom for other potential providers.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>CreatedByUserId</c> has no navigation property by design (ADR-004): profiles
///       must remain queryable even after the creating user is deleted.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class CredentialProfileConfiguration : IEntityTypeConfiguration<CredentialProfile>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CredentialProfile> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("CredentialProfiles");

        // ── Primary key ──────────────────────────────────────────────────────
        builder.HasKey(cp => cp.Id);

        // ── Properties ───────────────────────────────────────────────────────
        builder.Property(cp => cp.Id)
            .IsRequired()
            .ValueGeneratedNever(); // Application layer assigns Guid.NewGuid()

        builder.Property(cp => cp.Name)
            .IsRequired()
            .HasMaxLength(256);

        // AccessKeyId max 128 — per acceptance criteria.
        builder.Property(cp => cp.AccessKeyId)
            .IsRequired()
            .HasMaxLength(128);

        // EncryptedSecretKey stored as a raw byte array column (bytea / varbinary).
        // EF Core maps byte[] to the correct binary column type automatically for both
        // PostgreSQL (bytea) and MySQL (varbinary(MAX) / longblob depending on Pomelo version).
        builder.Property(cp => cp.EncryptedSecretKey)
            .IsRequired()
            .HasColumnType("bytea"); // ASSUMPTION: overridden per provider in migrations if needed;
                                     // 'bytea' is the PostgreSQL native type. MySQL uses varbinary
                                     // but EF Core's byte[] mapping handles this transparently via
                                     // Pomelo — the HasColumnType here is a documentation hint only
                                     // and will be superseded by provider-specific scaffolding.

        builder.Property(cp => cp.Region)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(cp => cp.BucketName)
            .IsRequired()
            .HasMaxLength(63); // S3 bucket name limit is 63 characters (AWS spec)

        builder.Property(cp => cp.KeyPrefix)
            .IsRequired(false)
            .HasMaxLength(1024);

        builder.Property(cp => cp.IsActive)
            .IsRequired();

        builder.Property(cp => cp.IsDeleted)
            .IsRequired();

        // UTC timestamps — normalised via AppDbContext.EnforceUtcDateTimes()
        builder.Property(cp => cp.CreatedAt)
            .IsRequired();

        builder.Property(cp => cp.UpdatedAt)
            .IsRequired();

        // Foreign key value stored without a navigation property (ADR-004).
        builder.Property(cp => cp.CreatedByUserId)
            .IsRequired();

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique partial index on Name where IsDeleted = false (idx_profile_name).
        // This prevents duplicate active profile names while allowing the same name
        // to be reused after a soft-delete (acceptance criteria exact wording).
        //
        // ASSUMPTION: EF Core's HasFilter() with a raw SQL predicate is the standard
        // mechanism for partial indexes. The filter expression uses the column name as
        // it will appear in the migration SQL — for PostgreSQL this is:
        //   WHERE "IsDeleted" = false
        // For MySQL, filtered unique indexes are not natively supported prior to 8.0.13
        // but the expression-based index syntax was introduced in MySQL 8.0.13+.
        // We use the PostgreSQL syntax here; the migration scaffold will need to be
        // reviewed for MySQL and may require a manual override.
        builder.HasIndex(cp => cp.Name)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("idx_profile_name");
    }
}
