using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileIgnoredMods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileIgnoredMods",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileIgnoredMods", x => new { x.RepoId, x.ProfileId, x.ModId });
                    table.ForeignKey(
                        name: "FK_ProfileIgnoredMods_Profiles_RepoId_ProfileId",
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
                name: "ProfileIgnoredMods");
        }
    }
}
