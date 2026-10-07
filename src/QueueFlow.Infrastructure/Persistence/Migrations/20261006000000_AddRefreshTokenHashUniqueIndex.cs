using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueueFlow.Infrastructure.Persistence;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261006000000_AddRefreshTokenHashUniqueIndex")]
public sealed class AddRefreshTokenHashUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_TokenHash",
            table: "RefreshTokens",
            column: "TokenHash",
            unique: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(
            name: "IX_RefreshTokens_TokenHash",
            table: "RefreshTokens");
}
