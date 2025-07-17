using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveToPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Repos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdapterData_Configuration = table.Column<string>(type: "text", nullable: false),
                    AdapterData_Id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfileLastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsTrusted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => new { x.RepoId, x.Id });
                    table.ForeignKey(
                        name: "FK_Profiles_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepoMemberships",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoMemberships", x => new { x.UserId, x.RepoId });
                    table.ForeignKey(
                        name: "FK_RepoMemberships_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepoMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModVersion",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModId = table.Column<string>(type: "text", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    ModVersionRepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModVersionModId = table.Column<string>(type: "text", nullable: false),
                    ModVersionId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModAttribute", x => new { x.ModVersionRepoId, x.ModVersionModId, x.ModVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_ModAttribute_ModVersion_ModVersionRepoId_ModVersionModId_Mo~",
                        columns: x => new { x.ModVersionRepoId, x.ModVersionModId, x.ModVersionId },
                        principalTable: "ModVersion",
                        principalColumns: new[] { "RepoId", "ModId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModDependency",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModId = table.Column<string>(type: "text", nullable: false),
                    ModVersionId = table.Column<string>(type: "text", nullable: false),
                    LockVersion = table.Column<bool>(type: "boolean", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_RepoId_Name",
                table: "Profiles",
                columns: new[] { "RepoId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepoMemberships_RepoId",
                table: "RepoMemberships",
                column: "RepoId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
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
                name: "RepoMemberships");

            migrationBuilder.DropTable(
                name: "ModVersion");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Mods");

            migrationBuilder.DropTable(
                name: "Repos");
        }
    }
}
