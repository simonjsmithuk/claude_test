using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataViewer.Infrastructure.Migrations.Postgresql
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        // ── Migration metadata ────────────────────────────────────────────────
        // Provider  : PostgreSQL ≥ 14 (via Npgsql.EntityFrameworkCore.PostgreSQL)
        // Namespace : DataViewer.Infrastructure.Migrations.Postgresql
        //
        // PostgreSQL-specific column types used:
        //   • uuid        — GUIDs (Guid properties) via Npgsql default mapping
        //   • bytea       — EncryptedSecretKey (byte[] with explicit HasColumnType)
        //   • jsonb       — AuditLogEntry.Parameters (binary JSON with GIN support)
        //   • timestamptz — DateTime properties (Npgsql maps DateTime → timestamptz)
        //   • boolean     — bool properties
        //   • integer     — int properties and enum-backed int columns
        //   • character varying(N) — string properties with HasMaxLength(N)
        //   • text        — string properties with no max-length constraint
        //
        // Indexes match the definitions in section 4.3 of the System Design Document
        // and the IEntityTypeConfiguration<T> fluent configurations:
        //   idx_user_username       — unique on Users.UserName
        //   idx_user_email          — non-unique on Users.Email
        //   idx_refresh_token_hash  — unique on RefreshTokens.TokenHash
        //   idx_refresh_user        — non-unique on RefreshTokens.UserId
        //   idx_profile_name        — unique partial on CredentialProfiles.Name WHERE "IsDeleted" = false
        //   idx_audit_user_timestamp     — composite (UserId ASC, TimestampUtc DESC)
        //   idx_audit_actiontype_timestamp — composite (ActionType ASC, TimestampUtc DESC)
        //   idx_audit_timestamp     — standalone TimestampUtc DESC
        //
        // Seed data:
        //   SystemSettings row with Id = 1 and default values (HasData in SystemSettingsConfiguration).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Users ─────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            // ── RefreshTokens ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    // Fixed-length SHA-256 hex string (64 chars); explicit maxLength
                    // produces character varying(64) which is index-friendly on PostgreSQL.
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── CredentialProfiles ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "CredentialProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AccessKeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    // bytea: PostgreSQL binary type for AES-encrypted Secret Access Key.
                    // Layout: [16-byte IV][ciphertext] — see CredentialProfile.IvSizeBytes.
                    EncryptedSecretKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BucketName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialProfiles", x => x.Id);
                });

            // ── AuditLogEntries ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    // jsonb: PostgreSQL binary JSON type.
                    // Advantages over json: deduplication, out-of-order key storage for faster
                    // operator evaluation, and GIN index support for @> containment queries.
                    Parameters = table.Column<string>(type: "jsonb", nullable: true),
                    ResultCount = table.Column<int>(type: "integer", nullable: true),
                    S3ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ProfileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            // ── UserPreferences ───────────────────────────────────────────────
            // Shared primary-key pattern: UserId is simultaneously PK and FK to Users.
            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultPageSize = table.Column<int>(type: "integer", nullable: false),
                    DefaultDateRangeDays = table.Column<int>(type: "integer", nullable: false),
                    PreferredProfileId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── SystemSettings ────────────────────────────────────────────────
            // Singleton table: always exactly one row with Id = 1.
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    JwtAccessTokenMinutes = table.Column<int>(type: "integer", nullable: false),
                    JwtRefreshTokenHours = table.Column<int>(type: "integer", nullable: false),
                    BodySizeCapMb = table.Column<int>(type: "integer", nullable: false),
                    LockoutThreshold = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            // ── Indexes: Users ────────────────────────────────────────────────

            // idx_user_username — unique index enforcing case-sensitive UserName uniqueness.
            // Application layer normalises UserName to lower-case before every read/write,
            // making this effectively case-insensitive without a provider-specific collation.
            migrationBuilder.CreateIndex(
                name: "idx_user_username",
                table: "Users",
                column: "UserName",
                unique: true);

            // idx_user_email — non-unique; supports alternative-login lookups by Email.
            migrationBuilder.CreateIndex(
                name: "idx_user_email",
                table: "Users",
                column: "Email");

            // ── Indexes: RefreshTokens ────────────────────────────────────────

            // idx_refresh_token_hash — unique; the primary lookup path for token exchange.
            migrationBuilder.CreateIndex(
                name: "idx_refresh_token_hash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            // idx_refresh_user — supports bulk-revocation queries per user.
            migrationBuilder.CreateIndex(
                name: "idx_refresh_user",
                table: "RefreshTokens",
                column: "UserId");

            // ── Indexes: CredentialProfiles ───────────────────────────────────

            // idx_profile_name — unique partial index on active (non-deleted) profiles.
            // The HasFilter expression uses PostgreSQL double-quoted identifier syntax.
            // Allows re-creating a profile with the same name after the original is
            // soft-deleted, while preventing duplicate active-profile names.
            migrationBuilder.CreateIndex(
                name: "idx_profile_name",
                table: "CredentialProfiles",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");

            // ── Indexes: AuditLogEntries ──────────────────────────────────────

            // idx_audit_user_timestamp — (UserId ASC, TimestampUtc DESC)
            // Primary audit query pattern: "all actions for user X, newest first".
            migrationBuilder.CreateIndex(
                name: "idx_audit_user_timestamp",
                table: "AuditLogEntries",
                columns: new[] { "UserId", "TimestampUtc" },
                descending: new[] { false, true });

            // idx_audit_actiontype_timestamp — (ActionType ASC, TimestampUtc DESC)
            // Secondary pattern: "all events of type Y, newest first".
            migrationBuilder.CreateIndex(
                name: "idx_audit_actiontype_timestamp",
                table: "AuditLogEntries",
                columns: new[] { "ActionType", "TimestampUtc" },
                descending: new[] { false, true });

            // idx_audit_timestamp — TimestampUtc DESC
            // Time-range-only dashboard queries without a leading equality filter.
            migrationBuilder.CreateIndex(
                name: "idx_audit_timestamp",
                table: "AuditLogEntries",
                column: "TimestampUtc",
                descending: new[] { true });

            // ── Seed: SystemSettings singleton row ────────────────────────────
            // Matches HasData() in SystemSettingsConfiguration.
            // DateTime.MinValue with UTC kind stored as 0001-01-01 00:00:00+00.
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "BodySizeCapMb", "JwtAccessTokenMinutes", "JwtRefreshTokenHours", "LockoutThreshold", "UpdatedAt" },
                values: new object[] { 1, 10, 15, 24, 5, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables in reverse dependency order to avoid FK constraint violations.
            // Cascade deletes handle dependent rows, but EF Core's Down() must still
            // drop tables in an order that satisfies FK constraints at the DDL level.

            migrationBuilder.DropTable(name: "AuditLogEntries");
            migrationBuilder.DropTable(name: "CredentialProfiles");
            migrationBuilder.DropTable(name: "SystemSettings");
            migrationBuilder.DropTable(name: "UserPreferences");
            migrationBuilder.DropTable(name: "RefreshTokens");
            migrationBuilder.DropTable(name: "Users");
        }
    }
}
