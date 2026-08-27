using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "AuditLogs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "Data",
                table: "AuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "AuditLogs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

#pragma warning disable CA1861
            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "OrganizationId", "CreatedAt" });
#pragma warning restore CA1861
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_OrganizationId_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Data",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "AuditLogs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
