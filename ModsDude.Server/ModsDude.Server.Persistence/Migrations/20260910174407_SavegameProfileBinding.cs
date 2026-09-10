using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavegameProfileBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Savegames_RepoId_ProfileId",
                table: "Savegames");

            migrationBuilder.AlterColumn<int>(
                name: "ProfileRevision",
                table: "SavegameVersions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProfileId",
                table: "SavegameVersions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProfileId",
                table: "Savegames",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "Savegames",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SavegameVersions_ProfileAndRevisionAreSetTogether",
                table: "SavegameVersions",
                sql: "(\"ProfileId\" IS NULL) = (\"ProfileRevision\" IS NULL)");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SavegameVersions_ProfileAndRevisionAreSetTogether",
                table: "SavegameVersions");

            migrationBuilder.DropIndex(
                name: "IX_Savegames_OneCurrentPerProfile",
                table: "Savegames");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Savegames_SupersededOnlyWithAProfile",
                table: "Savegames");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "Savegames");

            migrationBuilder.AlterColumn<int>(
                name: "ProfileRevision",
                table: "SavegameVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProfileId",
                table: "SavegameVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProfileId",
                table: "Savegames",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_RepoId_ProfileId",
                table: "Savegames",
                columns: new[] { "RepoId", "ProfileId" });
        }
    }
}
