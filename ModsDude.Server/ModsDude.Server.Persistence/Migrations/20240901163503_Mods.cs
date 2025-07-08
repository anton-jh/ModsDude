using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Mods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Profiles",
                table: "Profiles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Profiles",
                table: "Profiles",
                columns: new[] { "RepoId", "Id" });

            migrationBuilder.CreateTable(
                name: "Mods",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ModVersion",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RepoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModVersion", x => new { x.RepoId, x.ModId, x.Id });
                    table.ForeignKey(
                        name: "FK_ModVersion_Mods_RepoId_ModId",
                        columns: x => new { x.RepoId, x.ModId },
                        principalTable: "Mods",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModAttribute",
                columns: table => new
                {
                    ModVersionRepoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModVersionModId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ModVersionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModAttribute", x => new { x.ModVersionRepoId, x.ModVersionModId, x.ModVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_ModAttribute_ModVersion_ModVersionRepoId_ModVersionModId_ModVersionId",
                        columns: x => new { x.ModVersionRepoId, x.ModVersionModId, x.ModVersionId },
                        principalTable: "ModVersion",
                        principalColumns: new[] { "RepoId", "ModId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModDependency",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ModVersionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LockVersion = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModDependency", x => new { x.ProfileId, x.RepoId, x.ModId, x.ModVersionId });
                    table.ForeignKey(
                        name: "FK_ModDependency_ModVersion_RepoId_ModId_ModVersionId",
                        columns: x => new { x.RepoId, x.ModId, x.ModVersionId },
                        principalTable: "ModVersion",
                        principalColumns: new[] { "RepoId", "ModId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModDependency_Profiles_RepoId_ProfileId",
                        columns: x => new { x.RepoId, x.ProfileId },
                        principalTable: "Profiles",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModDependency_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ModId", "ModVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModDependency_RepoId_ProfileId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModVersion_RepoId_ModId_SequenceNumber",
                table: "ModVersion",
                columns: new[] { "RepoId", "ModId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModAttribute");

            migrationBuilder.DropTable(
                name: "ModDependency");

            migrationBuilder.DropTable(
                name: "ModVersion");

            migrationBuilder.DropTable(
                name: "Mods");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Profiles",
                table: "Profiles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Profiles",
                table: "Profiles",
                column: "Id");
        }
    }
}
