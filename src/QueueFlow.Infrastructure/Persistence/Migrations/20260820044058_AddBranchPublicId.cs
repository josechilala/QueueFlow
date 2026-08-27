using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Branches",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"Branches\" SET \"PublicId\" = replace(gen_random_uuid()::text, '-', '');");

            migrationBuilder.AlterColumn<string>(
                name: "PublicId",
                table: "Branches",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_PublicId",
                table: "Branches",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Branches_PublicId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Branches");
        }
    }
}
