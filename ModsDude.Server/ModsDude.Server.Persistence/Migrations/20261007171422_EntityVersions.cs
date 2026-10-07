using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EntityVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin was PostgreSQL's own system column, mapped rather than created, so there is nothing to drop.
            migrationBuilder.RenameColumn(
                name: "MembershipRevision",
                table: "Repos",
                newName: "MembersVersion");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "TrustCodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Savegames",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Repos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "RepoInvites",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IgnoredModsVersion",
                table: "Profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "TrustCodes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Savegames");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Repos");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "RepoInvites");

            migrationBuilder.DropColumn(
                name: "IgnoredModsVersion",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Profiles");

            migrationBuilder.RenameColumn(
                name: "MembersVersion",
                table: "Repos",
                newName: "MembershipRevision");
        }
    }
}
