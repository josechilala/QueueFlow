using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialRequests : Migration
    {
        private static readonly string[] StatusCreatedAtColumns = { "Status", "CreatedAt" };
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrialRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AcceptedTermsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByPlatformUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialRequests_OrganizationInvitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "OrganizationInvitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrialRequests_Email",
                table: "TrialRequests",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialRequests_InvitationId",
                table: "TrialRequests",
                column: "InvitationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialRequests_Status_CreatedAt",
                table: "TrialRequests",
                columns: StatusCreatedAtColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrialRequests");
        }
    }
}
