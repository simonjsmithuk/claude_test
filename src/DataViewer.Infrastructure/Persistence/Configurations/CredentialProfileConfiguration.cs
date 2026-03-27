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
///       <c>EncryptedSecretKey</c> is stored as a <c>byte[]</c> binary column.
///       The exact column type is provider-specific:
///       <list type="table">
///         <listheader><term>Provider</term><term>Column type</term></listheader>
///         <item><term>PostgreSQL</term><term><c>bytea</c></term></item>
///         <item><term>MySQL</term><term><c>longblob</c> (Pomelo default for <c>byte[]</c>)</term></item>
///       </list>
///       No <c>HasColumnType</c> annotation is emitted for MySQL because Pomelo maps
///       <c>byte[]</c> → <c>longblob</c> correctly without any explicit hint, and adding
///       the wrong hint (e.g. <c>"bytea"</c>) would produce an invalid migration.
///     </description>
///   </item>
///   <item>
///     <description>
///       The unique partial index <c>idx_profile_name</c> covers only rows where
///       <c>IsDeleted = false</c>. This allows a new profile with the same name to be
///       created after the original is soft-deleted, while still preventing duplicate
///       active names (Product Spec G-04 / soft-delete design).
///       The <c>HasFilter</c> expression is provider-conditional:
///       PostgreSQL uses double-quoted identifiers; MySQL 8.0.13+ supports functional
///       index expressions with backtick-quoted names. MySQL users prior to 8.0.13
///       will not have a partial index — the constraint degrades gracefully to a
///       non-filtered unique index in that case (see inline comment).
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
    private readonly string _databaseProvider;

    /// <summary>
    /// Initialises the configuration with the active database provider name so that
    /// provider-specific column type annotations and index filter expressions can be
    /// emitted correctly.
    /// </summary>
    /// <param name="databaseProvider">
    /// Lowercase provider identifier — <c>"postgresql"</c> or <c>"mysql"</c>.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="databaseProvider"/> is not a recognised value.
    /// </exception>
    public CredentialProfileConfiguration(string databaseProvider)
    {
        if (!databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) &&
            !databaseProvider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Unsupported database provider: '{databaseProvider}'. " +
                "Expected 'postgresql' or 'mysql'.");
        }

        _databaseProvider = databaseProvider;
    }

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

        // EncryptedSecretKey: stored as a raw binary column.
        // Byte layout: [16-byte AES IV][ciphertext] — see CredentialProfile.IvSizeBytes.
        //
        // PostgreSQL: explicit HasColumnType("bytea") is required because Npgsql does
        //             not infer the column type from byte[] without the annotation.
        // MySQL:      HasColumnType is omitted — Pomelo maps byte[] → longblob correctly
        //             without any annotation. Emitting "bytea" here would produce an
        //             invalid migration on MySQL because the type name does not exist.
        var encryptedKeyProp = builder.Property(cp => cp.EncryptedSecretKey)
            .IsRequired();

        if (_databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            encryptedKeyProp.HasColumnType("bytea");
        // MySQL: no HasColumnType — Pomelo's default byte[] → longblob mapping is correct.

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
        // Prevents duplicate active profile names while allowing reuse after soft-delete.
        //
        // The HasFilter expression uses provider-specific identifier quoting:
        //   PostgreSQL: double-quoted column names   → "IsDeleted" = false
        //   MySQL:      backtick-quoted column names → `IsDeleted` = 0
        //               (MySQL represents boolean false as integer 0)
        //               MySQL 8.0.13+ supports functional index expressions.
        //               On older MySQL versions this filter is ignored and the index
        //               degrades to a standard unique index on Name.
        var indexBuilder = builder.HasIndex(cp => cp.Name)
            .IsUnique()
            .HasDatabaseName("idx_profile_name");

        if (_databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            indexBuilder.HasFilter("\"IsDeleted\" = false");
        else
            indexBuilder.HasFilter("`IsDeleted` = 0"); // MySQL 8.0.13+ partial index syntax
    }
}
