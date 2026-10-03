using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavegamePublishRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavegamePublishRequests",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavegameId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CallerHoldsClaim = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavegamePublishRequests", x => new { x.RepoId, x.SavegameId });
                    table.ForeignKey(
                        name: "FK_SavegamePublishRequests_Savegames_RepoId_SavegameId",
                        columns: x => new { x.RepoId, x.SavegameId },
                        principalTable: "Savegames",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavegamePublishRequests_RequestId",
                table: "SavegamePublishRequests",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavegamePublishRequests");
        }
    }
}
