using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavegameVersionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavegameVersionDetails",
                columns: table => new
                {
                    SavegameVersionRepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavegameVersionSavegameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavegameVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavegameVersionDetails", x => new { x.SavegameVersionRepoId, x.SavegameVersionSavegameId, x.SavegameVersionNumber, x.Id });
                    table.ForeignKey(
                        name: "FK_SavegameVersionDetails_SavegameVersions_SavegameVersionRepo~",
                        columns: x => new { x.SavegameVersionRepoId, x.SavegameVersionSavegameId, x.SavegameVersionNumber },
                        principalTable: "SavegameVersions",
                        principalColumns: new[] { "RepoId", "SavegameId", "Number" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavegameVersionDetails");
        }
    }
}
