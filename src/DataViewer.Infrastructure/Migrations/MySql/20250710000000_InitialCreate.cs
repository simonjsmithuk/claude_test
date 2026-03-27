using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataViewer.Infrastructure.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        // ── Migration metadata ────────────────────────────────────────────────
        // Provider  : MySQL ≥ 8.0.13 (via Pomelo.EntityFrameworkCore.MySql)
        // Namespace : DataViewer.Infrastructure.Migrations.MySql
        //
        // MySQL-specific column types used:
        //   • char(36)    — GUIDs (Pomelo maps Guid → char(36) by default)
        //   • longblob    — EncryptedSecretKey (Pomelo's default byte[] mapping)
        //   • JSON        — AuditLogEntry.Parameters (MySQL 8.0+ validated JSON)
        //   • datetime(6) — DateTime properties (microsecond precision)
        //   • tinyint(1)  — bool properties (MySQL convention)
        //   • int         — int properties and enum-backed int columns
        //   • varchar(N)  — string properties with HasMaxLength(N)
        //   • longtext    — string properties with no max-length constraint
        //
        // Indexes match the definitions in section 4.3 of the System Design Document
        // and the IEntityTypeConfiguration<T> fluent configurations:
        //   idx_user_username       — unique on Users.UserName
        //   idx_user_email          — non-unique on Users.Email
        //   idx_refresh_token_hash  — unique on RefreshTokens.TokenHash
        //   idx_refresh_user        — non-unique on RefreshTokens.UserId
        //   idx_profile_name        — unique partial on CredentialProfiles.Name WHERE `IsDeleted` = 0
        //                             (MySQL 8.0.13+ functional index expression syntax)
        //   idx_audit_user_timestamp     — composite (UserId ASC, TimestampUtc DESC)
        //   idx_audit_actiontype_timestamp — composite (ActionType ASC, TimestampUtc DESC)
        //   idx_audit_timestamp     — standalone TimestampUtc DESC
        //
        // Seed data:
        //   SystemSettings row with Id = 1 and default values (HasData in SystemSettingsConfiguration).
        //
        // NOTE: MySQL does not support true partial/filtered indexes in the same way
        // PostgreSQL does. The idx_profile_name filter uses a MySQL 8.0.13+ functional
        // index expression. On MySQL < 8.0.13 the filter expression is silently ignored
        // and the index degrades to a standard unique index on Name.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Users ─────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Role = table.Column<int>(type: "int", nullable: false),
                    IsLocked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LockoutUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── RefreshTokens ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    // Fixed-length SHA-256 hex string (64 chars).
                    TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsRevoked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── CredentialProfiles ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "CredentialProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AccessKeyId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    // longblob: Pomelo's default mapping for byte[].
                    // Stores the AES-encrypted Secret Access Key: [16-byte IV][ciphertext].
                    // No explicit HasColumnType annotation is needed — Pomelo maps byte[] → longblob
                    // correctly by convention, and specifying "bytea" here would produce an
                    // invalid column type on MySQL.
                    EncryptedSecretKey = table.Column<byte[]>(type: "longblob", nullable: false),
                    Region = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BucketName = table.Column<string>(type: "varchar(63)", maxLength: 63, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyPrefix = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialProfiles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── AuditLogEntries ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IpAddress = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    // JSON: MySQL 8.0+ native validated JSON type.
                    // MySQL enforces well-formed JSON at the engine level and rejects invalid input.
                    // Unlike PostgreSQL's jsonb, MySQL JSON is stored as optimised binary internally
                    // but exposed as text; GIN indexes are not available.
                    Parameters = table.Column<string>(type: "JSON", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResultCount = table.Column<int>(type: "int", nullable: true),
                    S3ObjectKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── UserPreferences ───────────────────────────────────────────────
            // Shared primary-key pattern: UserId is simultaneously PK and FK to Users.
            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DefaultPageSize = table.Column<int>(type: "int", nullable: false),
                    DefaultDateRangeDays = table.Column<int>(type: "int", nullable: false),
                    PreferredProfileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── SystemSettings ────────────────────────────────────────────────
            // Singleton table: always exactly one row with Id = 1.
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    JwtAccessTokenMinutes = table.Column<int>(type: "int", nullable: false),
                    JwtRefreshTokenHours = table.Column<int>(type: "int", nullable: false),
                    BodySizeCapMb = table.Column<int>(type: "int", nullable: false),
                    LockoutThreshold = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── Indexes: Users ────────────────────────────────────────────────

            // idx_user_username — unique; application normalises UserName to lower-case
            // before every read/write for effective case-insensitive uniqueness.
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
            // MySQL 8.0.13+ supports functional index expressions via backtick quoting.
            // `IsDeleted` = 0 because MySQL represents boolean false as integer 0.
            // On MySQL < 8.0.13 the filter expression is silently ignored and the
            // index degrades to a standard unique index, which prevents any reuse of
            // names after soft-deletion — acceptable for older MySQL versions.
            migrationBuilder.CreateIndex(
                name: "idx_profile_name",
                table: "CredentialProfiles",
                column: "Name",
                unique: true,
                filter: "`IsDeleted` = 0");

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
            // MySQL datetime(6) represents DateTime.MinValue as 0001-01-01 00:00:00.000000.
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "BodySizeCapMb", "JwtAccessTokenMinutes", "JwtRefreshTokenHours", "LockoutThreshold", "UpdatedAt" },
                values: new object[] { 1, 10, 15, 24, 5, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables in reverse dependency order to avoid FK constraint violations.
            // MySQL enforces FK constraints during DDL operations; tables with FK
            // references must be dropped before their referenced principals.

            migrationBuilder.DropTable(name: "AuditLogEntries");
            migrationBuilder.DropTable(name: "CredentialProfiles");
            migrationBuilder.DropTable(name: "SystemSettings");
            migrationBuilder.DropTable(name: "UserPreferences");
            migrationBuilder.DropTable(name: "RefreshTokens");
            migrationBuilder.DropTable(name: "Users");
        }
    }
}
