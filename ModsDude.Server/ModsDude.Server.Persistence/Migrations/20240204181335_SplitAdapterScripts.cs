using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitAdapterScripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Adapter",
                table: "Repos");

            migrationBuilder.AddColumn<string>(
                name: "ModAdapter",
                table: "Repos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavegameAdapter",
                table: "Repos",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModAdapter",
                table: "Repos");

            migrationBuilder.DropColumn(
                name: "SavegameAdapter",
                table: "Repos");

            migrationBuilder.AddColumn<string>(
                name: "Adapter",
                table: "Repos",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
