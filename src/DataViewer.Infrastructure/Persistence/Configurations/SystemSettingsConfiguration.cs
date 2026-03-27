using DataViewer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataViewer.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent entity configuration for <see cref="SystemSettings"/>.
/// </summary>
/// <remarks>
/// Key decisions:
/// <list type="bullet">
///   <item>
///     <description>
///       A default seed row with <c>Id = 1</c> is applied via <c>HasData</c>.
///       This guarantees that the singleton row exists after every initial migration
///       so application code can unconditionally perform a single-row read against
///       <c>Id = 1</c> without a null-check guard (Product Spec G-05).
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>Id</c> has a private setter on the entity; EF Core can still set it via
///       reflection during materialisation. <c>ValueGeneratedNever()</c> ensures
///       EF Core treats the field as a client-assigned key and never attempts to
///       use database identity / auto-increment for it.
///     </description>
///   </item>
///   <item>
///     <description>
///       The seed <c>UpdatedAt</c> value is set to <c>DateTime.MinValue</c> (UTC)
///       as a deliberate sentinel: the Infrastructure interceptor will overwrite it
///       on the first real settings update. <c>DateTime.MinValue</c> in the database
///       is a clear signal that settings have never been explicitly saved since
///       initial deployment.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("SystemSettings");

        // ── Primary key ──────────────────────────────────────────────────────
        builder.HasKey(s => s.Id);

        // ── Properties ───────────────────────────────────────────────────────

        // Id has a private setter on the entity class — ValueGeneratedNever() tells
        // EF Core to treat it as a fully client-controlled key, never auto-increment.
        builder.Property(s => s.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(s => s.JwtAccessTokenMinutes)
            .IsRequired();

        builder.Property(s => s.JwtRefreshTokenHours)
            .IsRequired();

        builder.Property(s => s.BodySizeCapMb)
            .IsRequired();

        builder.Property(s => s.LockoutThreshold)
            .IsRequired();

        // UTC timestamp — normalised via AppDbContext.EnforceUtcDateTimes()
        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        // ── Seed data ────────────────────────────────────────────────────────

        // Seed a default singleton row with Id = 1 (acceptance criteria).
        // Default values mirror the SystemSettings property initialisers to keep
        // the in-memory defaults and the database defaults in sync.
        //
        // UpdatedAt is seeded as DateTime.MinValue (UTC): the Infrastructure
        // SaveChanges interceptor will overwrite this on the first admin update.
        // DateTime.MinValue with UTC kind ensures Npgsql does not reject the value.
        builder.HasData(new
        {
            Id = 1,
            JwtAccessTokenMinutes = 15,
            JwtRefreshTokenHours = 24,
            BodySizeCapMb = 10,
            LockoutThreshold = 5,
            UpdatedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
        });
    }
}
