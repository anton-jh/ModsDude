using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RevisionAndSnapshotRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RenameSnapshotRequests(migrationBuilder, from: "SavegameCheckInRequests", to: "SavegameSnapshotRequests");

            migrationBuilder.CreateTable(
                name: "ProfileRevisionRequests",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredWith = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileRevisionRequests", x => new { x.RepoId, x.ProfileId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ProfileRevisionRequests_Profiles_RepoId_ProfileId",
                        columns: x => new { x.RepoId, x.ProfileId },
                        principalTable: "Profiles",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileRevisionRequests");

            RenameSnapshotRequests(migrationBuilder, from: "SavegameSnapshotRequests", to: "SavegameCheckInRequests");
        }

        private static void RenameSnapshotRequests(MigrationBuilder migrationBuilder, string from, string to)
        {
            migrationBuilder.DropForeignKey(
                name: $"FK_{from}_Savegames_RepoId_SavegameId",
                table: from);

            migrationBuilder.DropPrimaryKey(
                name: $"PK_{from}",
                table: from);

            migrationBuilder.RenameTable(
                name: from,
                newName: to);

            migrationBuilder.AddPrimaryKey(
                name: $"PK_{to}",
                table: to,
                columns: new[] { "RepoId", "SavegameId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: $"FK_{to}_Savegames_RepoId_SavegameId",
                table: to,
                columns: new[] { "RepoId", "SavegameId" },
                principalTable: "Savegames",
                principalColumns: new[] { "RepoId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
