using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameActivities",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Game = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PinnedRevision = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    SavegameId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TouchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameActivities", x => new { x.UserId, x.Game });
                    table.ForeignKey(
                        name: "FK_GameActivities_Profiles_RepoId_ProfileId",
                        columns: x => new { x.RepoId, x.ProfileId },
                        principalTable: "Profiles",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameActivities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameActivities_RepoId_ProfileId",
                table: "GameActivities",
                columns: new[] { "RepoId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_GameActivities_RepoId_TouchedAt",
                table: "GameActivities",
                columns: new[] { "RepoId", "TouchedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameActivities");
        }
    }
}
