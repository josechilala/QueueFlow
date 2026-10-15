using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingWebhookInbox : Migration
    {
        private static readonly string[] DispatchColumns = ["Status", "NextAttemptAt"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingWebhookInbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingWebhookInbox", x => x.Id);
                    table.CheckConstraint("CK_BillingWebhookInbox_Attempts", "\"Attempts\" >= 0 AND \"Attempts\" <= 12");
                    table.CheckConstraint("CK_BillingWebhookInbox_Status", "\"Status\" IN ('Pending','Processed','Ignored','Review')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingWebhookInbox_ProviderEventId",
                table: "BillingWebhookInbox",
                column: "ProviderEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingWebhookInbox_Status_NextAttemptAt",
                table: "BillingWebhookInbox",
                columns: DispatchColumns);

            // Inbox receipts have no trusted tenant until processing. Block PostgREST/client roles;
            // the server-side table owner (or an explicitly privileged service role) processes them.
            migrationBuilder.Sql("ALTER TABLE \"BillingWebhookInbox\" ENABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingWebhookInbox");
        }
    }
}
