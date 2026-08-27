using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenQueueCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_QueueTickets_CustomerPublicToken",
                table: "QueueTickets",
                column: "CustomerPublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueTickets_QueueId_SequenceNumber",
                table: "QueueTickets",
                columns: ["QueueId", "SequenceNumber"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueTickets_CustomerPublicToken",
                table: "QueueTickets");

            migrationBuilder.DropIndex(
                name: "IX_QueueTickets_QueueId_SequenceNumber",
                table: "QueueTickets");
        }
    }
}
