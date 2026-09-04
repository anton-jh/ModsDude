using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Savegames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Savegames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HeadVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Savegames", x => new { x.RepoId, x.Id });
                    table.ForeignKey(
                        name: "FK_Savegames_Profiles_RepoId_ProfileId",
                        columns: x => new { x.RepoId, x.ProfileId },
                        principalTable: "Profiles",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Savegames_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavegameCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavegameId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TakenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavegameCheckouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavegameCheckouts_Savegames_RepoId_SavegameId",
                        columns: x => new { x.RepoId, x.SavegameId },
                        principalTable: "Savegames",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavegameVersions",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavegameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileRevision = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    BaseVersion = table.Column<int>(type: "integer", nullable: true),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavegameVersions", x => new { x.RepoId, x.SavegameId, x.Number });
                    table.ForeignKey(
                        name: "FK_SavegameVersions_ProfileRevisions_RepoId_ProfileId_ProfileR~",
                        columns: x => new { x.RepoId, x.ProfileId, x.ProfileRevision },
                        principalTable: "ProfileRevisions",
                        principalColumns: new[] { "RepoId", "ProfileId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavegameVersions_Savegames_RepoId_SavegameId",
                        columns: x => new { x.RepoId, x.SavegameId },
                        principalTable: "Savegames",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavegameCheckouts_RepoId_SavegameId",
                table: "SavegameCheckouts",
                columns: new[] { "RepoId", "SavegameId" },
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SavegameCheckouts_RepoId_SavegameId_TakenAt",
                table: "SavegameCheckouts",
                columns: new[] { "RepoId", "SavegameId", "TakenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_RepoId_Name",
                table: "Savegames",
                columns: new[] { "RepoId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Savegames_RepoId_ProfileId",
                table: "Savegames",
                columns: new[] { "RepoId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavegameVersions_RepoId_ProfileId_ProfileRevision",
                table: "SavegameVersions",
                columns: new[] { "RepoId", "ProfileId", "ProfileRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_SavegameVersions_RepoId_SavegameId_ContentHash",
                table: "SavegameVersions",
                columns: new[] { "RepoId", "SavegameId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavegameCheckouts");

            migrationBuilder.DropTable(
                name: "SavegameVersions");

            migrationBuilder.DropTable(
                name: "Savegames");
        }
    }
}
