using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobsAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
#pragma warning disable CA1861
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueMetricSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueId = table.Column<Guid>(type: "uuid", nullable: false),
                    WaitingCount = table.Column<int>(type: "integer", nullable: false),
                    ActiveAttendants = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueMetricSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OrganizationId",
                table: "OutboxMessages",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_NextAttemptAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueMetricSnapshots_OrganizationId",
                table: "QueueMetricSnapshots",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueMetricSnapshots_OrganizationId_QueueId_CreatedAt",
                table: "QueueMetricSnapshots",
                columns: new[] { "OrganizationId", "QueueId", "CreatedAt" });
#pragma warning restore CA1861
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "QueueMetricSnapshots");
        }
    }
}
