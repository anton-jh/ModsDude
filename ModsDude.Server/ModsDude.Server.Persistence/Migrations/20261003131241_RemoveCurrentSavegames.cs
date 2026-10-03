using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCurrentSavegames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Savegames_OneCurrentPerProfile",
                table: "Savegames");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Savegames_SupersededOnlyWithAProfile",
                table: "Savegames");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "Savegames");

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_RepoId_ProfileId",
                table: "Savegames",
                columns: new[] { "RepoId", "ProfileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Savegames_RepoId_ProfileId",
                table: "Savegames");

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "Savegames",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_OneCurrentPerProfile",
                table: "Savegames",
                columns: new[] { "RepoId", "ProfileId" },
                unique: true,
                filter: "\"SupersededAt\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Savegames_SupersededOnlyWithAProfile",
                table: "Savegames",
                sql: "\"SupersededAt\" IS NULL OR \"ProfileId\" IS NOT NULL");
        }
    }
}
