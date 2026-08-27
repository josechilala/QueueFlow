using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttendanceMode",
                table: "Services",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CustomerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScheduledEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfirmationCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NoShowAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RescheduledFromAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    QueueTicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentStatusHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BlockType = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceSchedulingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    CapacityPerSlot = table.Column<int>(type: "integer", nullable: false),
                    MinimumAdvanceMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaximumAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                    LateToleranceMinutes = table.Column<int>(type: "integer", nullable: false),
                    CancellationDeadlineMinutes = table.Column<int>(type: "integer", nullable: false),
                    CheckInAdvanceMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowCustomerCancellation = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCustomerReschedule = table.Column<bool>(type: "boolean", nullable: false),
                    RequireConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceSchedulingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId",
                table: "Appointments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId_BranchId_ServiceId_ScheduledSta~",
                table: "Appointments",
                columns: new[] { "OrganizationId", "BranchId", "ServiceId", "ScheduledStart" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId_Status_ScheduledStart",
                table: "Appointments",
                columns: new[] { "OrganizationId", "Status", "ScheduledStart" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PublicToken",
                table: "Appointments",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_QueueTicketId",
                table: "Appointments",
                column: "QueueTicketId",
                unique: true,
                filter: "\"QueueTicketId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentStatusHistory_AppointmentId_CreatedAt",
                table: "AppointmentStatusHistory",
                columns: new[] { "AppointmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentStatusHistory_OrganizationId",
                table: "AppointmentStatusHistory",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_OrganizationId",
                table: "ScheduleBlocks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_OrganizationId_BranchId_StartAt_EndAt",
                table: "ScheduleBlocks",
                columns: new[] { "OrganizationId", "BranchId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSchedules_OrganizationId",
                table: "ServiceSchedules",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSchedules_OrganizationId_BranchId_ServiceId_DayOfWeek",
                table: "ServiceSchedules",
                columns: new[] { "OrganizationId", "BranchId", "ServiceId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSchedulingSettings_OrganizationId",
                table: "ServiceSchedulingSettings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSchedulingSettings_OrganizationId_ServiceId",
                table: "ServiceSchedulingSettings",
                columns: new[] { "OrganizationId", "ServiceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "AppointmentStatusHistory");

            migrationBuilder.DropTable(
                name: "ScheduleBlocks");

            migrationBuilder.DropTable(
                name: "ServiceSchedules");

            migrationBuilder.DropTable(
                name: "ServiceSchedulingSettings");

            migrationBuilder.DropColumn(
                name: "AttendanceMode",
                table: "Services");

        }
    }
}
