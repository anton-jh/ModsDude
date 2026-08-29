using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlattenModModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModAttribute_ModVersion_ModVersionRepoId_ModVersionModId_Mo~",
                table: "ModAttribute");

            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ModVersion_RepoId_ModId_ModVersionId",
                table: "ModDependency");

            migrationBuilder.DropForeignKey(
                name: "FK_ModVersion_Mods_RepoId_ModId",
                table: "ModVersion");

            migrationBuilder.DropTable(
                name: "Mods");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency");

            migrationBuilder.DropIndex(
                name: "IX_ModDependency_RepoId_ProfileId",
                table: "ModDependency");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModVersion",
                table: "ModVersion");

            migrationBuilder.RenameTable(
                name: "ModVersion",
                newName: "ModVersions");

            migrationBuilder.RenameColumn(
                name: "LockVersion",
                table: "ModDependency",
                newName: "Locked");

            migrationBuilder.RenameIndex(
                name: "IX_ModVersion_RepoId_ModId_SequenceNumber",
                table: "ModVersions",
                newName: "IX_ModVersions_RepoId_ModId_SequenceNumber");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "ModVersions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Locked",
                table: "ModVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Updated",
                table: "ModVersions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "ModId", "ModVersionId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModVersions",
                table: "ModVersions",
                columns: new[] { "RepoId", "ModId", "Id" });

            migrationBuilder.CreateTable(
                name: "ModImageReference",
                columns: table => new
                {
                    ModVersionRepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModVersionModId = table.Column<string>(type: "text", nullable: false),
                    ModVersionId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModImageReference", x => new { x.ModVersionRepoId, x.ModVersionModId, x.ModVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_ModImageReference_ModVersions_ModVersionRepoId_ModVersionMo~",
                        columns: x => new { x.ModVersionRepoId, x.ModVersionModId, x.ModVersionId },
                        principalTable: "ModVersions",
                        principalColumns: new[] { "RepoId", "ModId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModDependency_RepoId_ProfileId_ModId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "ModId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ModAttribute_ModVersions_ModVersionRepoId_ModVersionModId_M~",
                table: "ModAttribute",
                columns: new[] { "ModVersionRepoId", "ModVersionModId", "ModVersionId" },
                principalTable: "ModVersions",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ModId", "ModVersionId" },
                principalTable: "ModVersions",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ModVersions_Repos_RepoId",
                table: "ModVersions",
                column: "RepoId",
                principalTable: "Repos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModAttribute_ModVersions_ModVersionRepoId_ModVersionModId_M~",
                table: "ModAttribute");

            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency");

            migrationBuilder.DropForeignKey(
                name: "FK_ModVersions_Repos_RepoId",
                table: "ModVersions");

            migrationBuilder.DropTable(
                name: "ModImageReference");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency");

            migrationBuilder.DropIndex(
                name: "IX_ModDependency_RepoId_ProfileId_ModId",
                table: "ModDependency");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModVersions",
                table: "ModVersions");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "ModVersions");

            migrationBuilder.DropColumn(
                name: "Locked",
                table: "ModVersions");

            migrationBuilder.DropColumn(
                name: "Updated",
                table: "ModVersions");

            migrationBuilder.RenameTable(
                name: "ModVersions",
                newName: "ModVersion");

            migrationBuilder.RenameColumn(
                name: "Locked",
                table: "ModDependency",
                newName: "LockVersion");

            migrationBuilder.RenameIndex(
                name: "IX_ModVersions_RepoId_ModId_SequenceNumber",
                table: "ModVersion",
                newName: "IX_ModVersion_RepoId_ModId_SequenceNumber");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency",
                columns: new[] { "ProfileId", "RepoId", "ModId", "ModVersionId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModVersion",
                table: "ModVersion",
                columns: new[] { "RepoId", "ModId", "Id" });

            migrationBuilder.CreateTable(
                name: "Mods",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mods", x => new { x.RepoId, x.Id });
                    table.ForeignKey(
                        name: "FK_Mods_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModDependency_RepoId_ProfileId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ModAttribute_ModVersion_ModVersionRepoId_ModVersionModId_Mo~",
                table: "ModAttribute",
                columns: new[] { "ModVersionRepoId", "ModVersionModId", "ModVersionId" },
                principalTable: "ModVersion",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ModVersion_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ModId", "ModVersionId" },
                principalTable: "ModVersion",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ModVersion_Mods_RepoId_ModId",
                table: "ModVersion",
                columns: new[] { "RepoId", "ModId" },
                principalTable: "Mods",
                principalColumns: new[] { "RepoId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
