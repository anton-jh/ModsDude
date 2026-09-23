using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModHubMods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModHubCrawlStates",
                columns: table => new
                {
                    Game = table.Column<string>(type: "text", nullable: false),
                    SweepNextPage = table.Column<int>(type: "integer", nullable: true),
                    SweepCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HeadModIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    PolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModHubCrawlStates", x => x.Game);
                });

            migrationBuilder.CreateTable(
                name: "ModHubMods",
                columns: table => new
                {
                    Game = table.Column<string>(type: "text", nullable: false),
                    ModHubId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FileNameKey = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Released = table.Column<DateOnly>(type: "date", nullable: true),
                    Size = table.Column<string>(type: "text", nullable: true),
                    DownloadUrl = table.Column<string>(type: "text", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModHubMods", x => new { x.Game, x.ModHubId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModHubMods_Game_FetchedAt",
                table: "ModHubMods",
                columns: new[] { "Game", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ModHubMods_Game_FileNameKey",
                table: "ModHubMods",
                columns: new[] { "Game", "FileNameKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModHubCrawlStates");

            migrationBuilder.DropTable(
                name: "ModHubMods");
        }
    }
}
