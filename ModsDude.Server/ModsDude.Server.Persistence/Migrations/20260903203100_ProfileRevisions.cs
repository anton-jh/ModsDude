using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_Profiles_RepoId_ProfileId",
                table: "ModDependency");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency");

            migrationBuilder.DropIndex(
                name: "IX_ModDependency_RepoId_ProfileId_ModId",
                table: "ModDependency");

            migrationBuilder.AddColumn<int>(
                name: "HeadRevision",
                table: "Profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "ModDependency",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber", "ModId", "ModVersionId" });

            migrationBuilder.CreateTable(
                name: "ProfileRevisions",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ModCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    SourceProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: true),
                    Changes_Added = table.Column<int>(type: "integer", nullable: false),
                    Changes_Changed = table.Column<int>(type: "integer", nullable: false),
                    Changes_Removed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileRevisions", x => new { x.RepoId, x.ProfileId, x.Number });
                    table.ForeignKey(
                        name: "FK_ProfileRevisions_Profiles_RepoId_ProfileId",
                        columns: x => new { x.RepoId, x.ProfileId },
                        principalTable: "Profiles",
                        principalColumns: new[] { "RepoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            // Every profile that already exists becomes a profile with one revision, holding exactly
            // what it holds now. Written by hand because the alternative - letting the columns keep
            // their zero defaults - would leave every dependency row pointing at a revision that
            // does not exist, which the foreign key added below refuses.
            //
            // The author is recorded as 'unknown' rather than invented: these lists were assembled
            // before anything recorded who was assembling them, and no user id would be true. It is
            // not a subject any identity provider issues, so it cannot collide with a real one, and
            // the history renders it as the name because no user row resolves it.
            migrationBuilder.Sql("""
                INSERT INTO "ProfileRevisions" (
                    "RepoId", "ProfileId", "Number", "ModCount", "CreatedBy", "Created", "Label", "Origin",
                    "SourceProfileId", "SourceRevision", "Changes_Added", "Changes_Changed", "Changes_Removed")
                SELECT
                    p."RepoId", p."Id", 1, COALESCE(d."ModCount", 0), 'unknown', p."Created", NULL, 'Created',
                    NULL, NULL, COALESCE(d."ModCount", 0), 0, 0
                FROM "Profiles" p
                LEFT JOIN (
                    SELECT "RepoId", "ProfileId", COUNT(*) AS "ModCount"
                    FROM "ModDependency"
                    GROUP BY "RepoId", "ProfileId"
                ) d ON d."RepoId" = p."RepoId" AND d."ProfileId" = p."Id";
                """);

            migrationBuilder.Sql("""UPDATE "Profiles" SET "HeadRevision" = 1;""");
            migrationBuilder.Sql("""UPDATE "ModDependency" SET "RevisionNumber" = 1;""");

            migrationBuilder.CreateIndex(
                name: "IX_ModDependency_RepoId_ProfileId_RevisionNumber_ModId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber", "ModId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ProfileRevisions_RepoId_ProfileId_RevisionNum~",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber" },
                principalTable: "ProfileRevisions",
                principalColumns: new[] { "RepoId", "ProfileId", "Number" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ProfileRevisions_RepoId_ProfileId_RevisionNum~",
                table: "ModDependency");

            migrationBuilder.DropTable(
                name: "ProfileRevisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency");

            migrationBuilder.DropIndex(
                name: "IX_ModDependency_RepoId_ProfileId_RevisionNumber_ModId",
                table: "ModDependency");

            migrationBuilder.DropColumn(
                name: "HeadRevision",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "ModDependency");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "ModId", "ModVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModDependency_RepoId_ProfileId_ModId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "ModId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_Profiles_RepoId_ProfileId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId" },
                principalTable: "Profiles",
                principalColumns: new[] { "RepoId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
