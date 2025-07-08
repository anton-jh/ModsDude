using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdapterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModAdapter",
                table: "Repos");

            migrationBuilder.DropColumn(
                name: "SavegameAdapter",
                table: "Repos");

            migrationBuilder.AddColumn<string>(
                name: "AdapterData_Configuration",
                table: "Repos",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdapterData_Id",
                table: "Repos",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdapterData_Configuration",
                table: "Repos");

            migrationBuilder.DropColumn(
                name: "AdapterData_Id",
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
    }
}
