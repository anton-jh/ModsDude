using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileModAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Added",
                table: "ModDependency",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // History already says when every mod arrived, so it is read rather than guessed at: a
            // mod's date is the instant of the first revision in its current unbroken run at one
            // version. A run breaks when the version changes and when a revision that still exists
            // lacks the mod; a revision deleted from the middle breaks nothing, which is also how the
            // date is carried forward once it is being stamped.
            migrationBuilder.Sql("""
                WITH history AS (
                    SELECT d."RepoId", d."ProfileId", d."RevisionNumber", d."ModId", d."ModVersionId", r."Created",
                        LAG(d."RevisionNumber") OVER w AS "PreviousRevision",
                        LAG(d."ModVersionId") OVER w AS "PreviousVersion"
                    FROM "ModDependency" d
                    JOIN "ProfileRevisions" r
                        ON r."RepoId" = d."RepoId" AND r."ProfileId" = d."ProfileId" AND r."Number" = d."RevisionNumber"
                    WINDOW w AS (PARTITION BY d."RepoId", d."ProfileId", d."ModId" ORDER BY d."RevisionNumber")
                ),
                boundaries AS (
                    SELECT h.*,
                        CASE
                            WHEN h."PreviousRevision" IS NULL OR h."PreviousVersion" <> h."ModVersionId" THEN 1
                            WHEN EXISTS (
                                SELECT 1 FROM "ProfileRevisions" x
                                WHERE x."RepoId" = h."RepoId" AND x."ProfileId" = h."ProfileId"
                                    AND x."Number" > h."PreviousRevision" AND x."Number" < h."RevisionNumber") THEN 1
                            ELSE 0
                        END AS "Starts"
                    FROM history h
                ),
                runs AS (
                    SELECT b.*,
                        SUM(b."Starts") OVER (
                            PARTITION BY b."RepoId", b."ProfileId", b."ModId" ORDER BY b."RevisionNumber") AS "Run"
                    FROM boundaries b
                ),
                stamps AS (
                    SELECT "RepoId", "ProfileId", "RevisionNumber", "ModId", "ModVersionId",
                        MIN("Created") OVER (PARTITION BY "RepoId", "ProfileId", "ModId", "Run") AS "Added"
                    FROM runs
                )
                UPDATE "ModDependency" d
                SET "Added" = s."Added"
                FROM stamps s
                WHERE d."RepoId" = s."RepoId" AND d."ProfileId" = s."ProfileId" AND d."RevisionNumber" = s."RevisionNumber"
                    AND d."ModId" = s."ModId" AND d."ModVersionId" = s."ModVersionId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Added",
                table: "ModDependency");
        }
    }
}
