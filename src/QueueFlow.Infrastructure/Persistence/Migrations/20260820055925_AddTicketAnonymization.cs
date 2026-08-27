using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260820055925_AddTicketAnonymization")]
public sealed class AddTicketAnonymization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "CustomerPublicToken", table: "QueueTickets", type: "character varying(100)", maxLength: 100, nullable: false, oldClrType: typeof(string), oldType: "text");
        migrationBuilder.AlterColumn<string>(name: "CustomerPhone", table: "QueueTickets", type: "character varying(30)", maxLength: 30, nullable: true, oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "CustomerName", table: "QueueTickets", type: "character varying(200)", maxLength: 200, nullable: true, oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "AnonymizedAt", table: "QueueTickets", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AnonymizedAt", table: "QueueTickets");
        migrationBuilder.AlterColumn<string>(name: "CustomerPublicToken", table: "QueueTickets", type: "text", nullable: false, oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);
        migrationBuilder.AlterColumn<string>(name: "CustomerPhone", table: "QueueTickets", type: "text", nullable: true, oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "CustomerName", table: "QueueTickets", type: "text", nullable: true, oldClrType: typeof(string), oldType: "character varying(200)", oldMaxLength: 200, oldNullable: true);
    }
}
