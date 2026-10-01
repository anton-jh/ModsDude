using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavegameCheckInRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavegameCheckInRequests",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavegameId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredWith = table.Column<int>(type: "integer", nullable: false),
                    CallerHoldsClaim = table.Column<bool>(type: "boolean", nullable: false),
                    TakenFrom = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavegameCheckInRequests", x => new { x.RepoId, x.SavegameId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SavegameCheckInRequests_Savegames_RepoId_SavegameId",
                        columns: x => new { x.RepoId, x.SavegameId },
                        principalTable: "Savegames",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavegameCheckInRequests");
        }
    }
}
