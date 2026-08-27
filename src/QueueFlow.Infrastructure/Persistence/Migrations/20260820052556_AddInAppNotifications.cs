using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInAppNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "Notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientPublicToken",
                table: "Notifications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

#pragma warning disable CA1861
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganizationId_RecipientPublicToken_CreatedAt",
                table: "Notifications",
                columns: new[] { "OrganizationId", "RecipientPublicToken", "CreatedAt" });
#pragma warning restore CA1861
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_OrganizationId_RecipientPublicToken_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Message",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RecipientPublicToken",
                table: "Notifications");
        }
    }
}
