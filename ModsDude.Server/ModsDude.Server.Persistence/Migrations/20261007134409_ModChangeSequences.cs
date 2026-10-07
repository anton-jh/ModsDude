using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModChangeSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModVersions_RepoId_Updated_ModId_Id",
                table: "ModVersions");

            migrationBuilder.AddColumn<long>(
                name: "ChangeSequence",
                table: "ModVersions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ModVersionDeletions",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeSequence = table.Column<long>(type: "bigint", nullable: false),
                    ModId = table.Column<string>(type: "text", nullable: false),
                    VersionId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModVersionDeletions", x => new { x.RepoId, x.ChangeSequence });
                    table.ForeignKey(
                        name: "FK_ModVersionDeletions_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepoChangeCounters",
                columns: table => new
                {
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mods = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoChangeCounters", x => x.RepoId);
                    table.ForeignKey(
                        name: "FK_RepoChangeCounters_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing versions numbered 1..n per repo, and each repo's counter started after them, so
            // the index below can be unique.
            migrationBuilder.Sql("""
                UPDATE "ModVersions" AS v
                SET "ChangeSequence" = numbered.n
                FROM (
                    SELECT "RepoId", "ModId", "Id",
                           ROW_NUMBER() OVER (PARTITION BY "RepoId" ORDER BY "Updated", "ModId", "Id") AS n
                    FROM "ModVersions"
                ) AS numbered
                WHERE v."RepoId" = numbered."RepoId" AND v."ModId" = numbered."ModId" AND v."Id" = numbered."Id";

                INSERT INTO "RepoChangeCounters" ("RepoId", "Mods")
                SELECT "RepoId", MAX("ChangeSequence") FROM "ModVersions" GROUP BY "RepoId";
                """);

            // The counter row stays locked until the writing transaction commits, so numbers are handed
            // out in commit order within a repo.
            migrationBuilder.Sql("""
                CREATE FUNCTION next_mod_change(repo uuid) RETURNS bigint AS $$
                    INSERT INTO "RepoChangeCounters" ("RepoId", "Mods") VALUES (repo, 1)
                    ON CONFLICT ("RepoId") DO UPDATE SET "Mods" = "RepoChangeCounters"."Mods" + 1
                    RETURNING "Mods";
                $$ LANGUAGE sql;

                CREATE FUNCTION stamp_mod_version_change() RETURNS trigger AS $$
                BEGIN
                    NEW."ChangeSequence" := next_mod_change(NEW."RepoId");
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER stamp_mod_version_change
                    BEFORE INSERT OR UPDATE ON "ModVersions"
                    FOR EACH ROW EXECUTE FUNCTION stamp_mod_version_change();

                CREATE FUNCTION log_mod_version_deletion() RETURNS trigger AS $$
                BEGIN
                    INSERT INTO "ModVersionDeletions" ("RepoId", "ModId", "VersionId", "ChangeSequence")
                    VALUES (OLD."RepoId", OLD."ModId", OLD."Id", next_mod_change(OLD."RepoId"));
                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER log_mod_version_deletion
                    AFTER DELETE ON "ModVersions"
                    FOR EACH ROW EXECUTE FUNCTION log_mod_version_deletion();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ModVersions_RepoId_ChangeSequence",
                table: "ModVersions",
                columns: new[] { "RepoId", "ChangeSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER log_mod_version_deletion ON "ModVersions";
                DROP TRIGGER stamp_mod_version_change ON "ModVersions";
                DROP FUNCTION log_mod_version_deletion();
                DROP FUNCTION stamp_mod_version_change();
                DROP FUNCTION next_mod_change(uuid);
                """);

            migrationBuilder.DropTable(
                name: "ModVersionDeletions");

            migrationBuilder.DropTable(
                name: "RepoChangeCounters");

            migrationBuilder.DropIndex(
                name: "IX_ModVersions_RepoId_ChangeSequence",
                table: "ModVersions");

            migrationBuilder.DropColumn(
                name: "ChangeSequence",
                table: "ModVersions");

            migrationBuilder.CreateIndex(
                name: "IX_ModVersions_RepoId_Updated_ModId_Id",
                table: "ModVersions",
                columns: new[] { "RepoId", "Updated", "ModId", "Id" });
        }
    }
}
