using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveQueuePerService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Queues" WHERE "IsActive" = true
                        GROUP BY "OrganizationId", "BranchId", "ServiceId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Multiple active queues exist for a service. Resolve duplicates explicitly before applying UX_Queues_ActiveService; no data was changed.';
                    END IF;
                END $$;
                """);
            migrationBuilder.CreateIndex(
                name: "UX_Queues_ActiveService",
                table: "Queues",
                columns: ["OrganizationId", "BranchId", "ServiceId"],
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Queues_ActiveService",
                table: "Queues");
        }
    }
}
