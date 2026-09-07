using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Archiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Savegames_RepoId_Name",
                table: "Savegames");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_RepoId_Name",
                table: "Profiles");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Savegames",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Repos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_RepoId_Name",
                table: "Savegames",
                columns: new[] { "RepoId", "Name" },
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Repos_Name",
                table: "Repos",
                column: "Name",
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_RepoId_Name",
                table: "Profiles",
                columns: new[] { "RepoId", "Name" },
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Savegames_RepoId_Name",
                table: "Savegames");

            migrationBuilder.DropIndex(
                name: "IX_Repos_Name",
                table: "Repos");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_RepoId_Name",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Savegames");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Repos");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Profiles");

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_RepoId_Name",
                table: "Savegames",
                columns: new[] { "RepoId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_RepoId_Name",
                table: "Profiles",
                columns: new[] { "RepoId", "Name" },
                unique: true);
        }
    }
}
