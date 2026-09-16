using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureActivationAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivationAuthorizationConsumedAt",
                table: "OrganizationInvitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivationAuthorizationExpiresAt",
                table: "OrganizationInvitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActivationAuthorizationHash",
                table: "OrganizationInvitations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ActivationAuthorizationConsumedAt",
                table: "OrganizationInvitations");

            migrationBuilder.DropColumn(
                name: "ActivationAuthorizationExpiresAt",
                table: "OrganizationInvitations");

            migrationBuilder.DropColumn(
                name: "ActivationAuthorizationHash",
                table: "OrganizationInvitations");
        }
    }
}
