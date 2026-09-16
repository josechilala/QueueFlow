using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAdministrationAndInvitations : Migration
    {
        private static readonly string[] InvitationEmailExpiresAtColumns = { "Email", "ExpiresAt" };
        private static readonly string[] AuditUserCreatedAtColumns = { "PlatformUserId", "CreatedAt" };
        private static readonly string[] RefreshTokenUserExpiresAtColumns = { "PlatformUserId", "ExpiresAt" };
        private static readonly string[] UserOrganizationEmailColumns = { "OrganizationId", "Email" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InvariantEmailNormalization.CreateFunctionSql());
            migrationBuilder.Sql("LOCK TABLE \"Users\" IN SHARE ROW EXCLUSIVE MODE;");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        GROUP BY queueflow_normalize_email("Email") COLLATE "C"
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot create global Users.Email unique index: duplicate normalized emails exist. Run scripts/preflight-global-user-email.sql and resolve duplicates first.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "Users" WHERE "Email" COLLATE "C" <> queueflow_normalize_email("Email") COLLATE "C") THEN
                        RAISE EXCEPTION 'Noncanonical user emails exist. Review the preflight report; migration never changes user data automatically.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_OrganizationId_Email",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "OrganizationInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ResponsibleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrganizationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Plan = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerificationCodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerificationCodeExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationCodeSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Data = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_ActivatedOrganizationId",
                table: "OrganizationInvitations",
                column: "ActivatedOrganizationId",
                unique: true,
                filter: "\"ActivatedOrganizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_Email_ExpiresAt",
                table: "OrganizationInvitations",
                columns: InvitationEmailExpiresAtColumns);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_TokenHash",
                table: "OrganizationInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditLogs_CreatedAt",
                table: "PlatformAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditLogs_PlatformUserId_CreatedAt",
                table: "PlatformAuditLogs",
                columns: AuditUserCreatedAtColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRefreshTokens_PlatformUserId_ExpiresAt",
                table: "PlatformRefreshTokens",
                columns: RefreshTokenUserExpiresAtColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRefreshTokens_TokenHash",
                table: "PlatformRefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUsers_Email",
                table: "PlatformUsers",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS queueflow_normalize_email(text);");
            migrationBuilder.DropTable(
                name: "OrganizationInvitations");

            migrationBuilder.DropTable(
                name: "PlatformAuditLogs");

            migrationBuilder.DropTable(
                name: "PlatformRefreshTokens");

            migrationBuilder.DropTable(
                name: "PlatformUsers");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationId_Email",
                table: "Users",
                columns: UserOrganizationEmailColumns,
                unique: true);
        }
    }
}
