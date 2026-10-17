using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileDismissedVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IgnoredModsVersion",
                table: "Profiles",
                newName: "IgnoredVersion");

            migrationBuilder.CreateTable(
                name: "ProfileDismissedVersions",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModId = table.Column<string>(type: "text", nullable: false),
                    VersionId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileDismissedVersions", x => new { x.RepoId, x.ProfileId, x.ModId, x.VersionId });
                    table.ForeignKey(
                        name: "FK_ProfileDismissedVersions_Profiles_RepoId_ProfileId",
                        columns: x => new { x.RepoId, x.ProfileId },
                        principalTable: "Profiles",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileDismissedVersions");

            migrationBuilder.RenameColumn(
                name: "IgnoredVersion",
                table: "Profiles",
                newName: "IgnoredModsVersion");
        }
    }
}
