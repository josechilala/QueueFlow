using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCharges : Migration
    {
        private static readonly string[] TenantCreatedColumns = ["OrganizationId", "CreatedAt"];
        private static readonly string[] TenantIdempotencyColumns = ["OrganizationId", "IdempotencyKey"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AsaasPaymentId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AsaasCustomerId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AsaasSubscriptionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCharges", x => x.Id);
                    table.CheckConstraint("CK_BillingCharges_AmountCurrency", "\"Amount\" > 0 AND \"Currency\" = 'BRL'");
                    table.CheckConstraint("CK_BillingCharges_Selection", "\"PlanCode\" IN ('professional','enterprise') AND \"Cycle\" IN ('Monthly','Yearly') AND \"PaymentMethod\" IN ('Pix','CreditCard')");
                    table.CheckConstraint("CK_BillingCharges_Status", "\"Status\" IN ('Pending','Uncertain','Paid','Cancelled','Refunded') AND (\"Status\" <> 'Paid' OR (\"PaidAt\" IS NOT NULL AND \"AsaasPaymentId\" IS NOT NULL))");
                    table.ForeignKey("FK_BillingCharges_Organizations_OrganizationId", x => x.OrganizationId,
                        "Organizations", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingWebhookEvents", x => x.Id);
                    table.ForeignKey("FK_BillingWebhookEvents_BillingCharges_ChargeId", x => x.ChargeId,
                        "BillingCharges", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_BillingWebhookEvents_ChargeId", "BillingWebhookEvents", "ChargeId");
            migrationBuilder.CreateIndex("IX_BillingWebhookEvents_ProviderEventId", "BillingWebhookEvents", "ProviderEventId", unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_AsaasPaymentId",
                table: "BillingCharges",
                column: "AsaasPaymentId",
                unique: true,
                filter: "\"AsaasPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_AsaasSubscriptionId",
                table: "BillingCharges",
                column: "AsaasSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_OrganizationId_CreatedAt",
                table: "BillingCharges",
                columns: TenantCreatedColumns);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_OrganizationId_IdempotencyKey",
                table: "BillingCharges",
                columns: TenantIdempotencyColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BillingWebhookEvents");
            migrationBuilder.DropTable(
                name: "BillingCharges");
        }
    }
}
