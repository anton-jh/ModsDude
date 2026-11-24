using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlobDeletionQueueWithoutDelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION queue_blob_deletion(text, text, text, uuid, text, text, text, timestamp with time zone);
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlobDeletionCandidates_AdminIsComplete",
                table: "BlobDeletionCandidates");

            migrationBuilder.DropColumn(
                name: "FirstSeen",
                table: "BlobDeletionCandidates");

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAt",
                table: "BlobDeletionCandidates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlobDeletionCandidates_AdminIsComplete",
                table: "BlobDeletionCandidates",
                sql: "\"Reason\" <> 'Admin' OR (\"ExpectedVersion\" IS NOT NULL AND \"RequestedBy\" IS NOT NULL)");

            // One row per blob, through one function, for triggers and the API alike. Queuing a blob
            // that is queued already moves QueuedAt and counts the requeue, so a deletion run that read
            // the row before never drops it. Each trigger queues the blob a row referred to when the row
            // is deleted, by any statement and by any cascade, or stops referring to it. The names follow
            // BlobNames; the persistence tests hold the two together.
            migrationBuilder.Sql("""
                CREATE FUNCTION queue_blob_deletion(
                    p_container text, p_blob_name text, p_reason text, p_repo_id uuid, p_user_id text,
                    p_requested_by text, p_expected_version text)
                RETURNS void AS $$
                    INSERT INTO "BlobDeletionCandidates"
                        ("Container", "BlobName", "Reason", "RepoId", "UserId", "RequestedBy", "ExpectedVersion", "QueuedAt", "Requeued")
                    VALUES (p_container, p_blob_name, p_reason, p_repo_id, p_user_id, p_requested_by, p_expected_version, now(), 0)
                    ON CONFLICT ("Container", "BlobName") DO UPDATE SET
                        "Reason" = EXCLUDED."Reason",
                        "RepoId" = EXCLUDED."RepoId",
                        "UserId" = EXCLUDED."UserId",
                        "RequestedBy" = EXCLUDED."RequestedBy",
                        "ExpectedVersion" = EXCLUDED."ExpectedVersion",
                        "QueuedAt" = EXCLUDED."QueuedAt",
                        "Requeued" = "BlobDeletionCandidates"."Requeued" + 1;
                $$ LANGUAGE sql;
                """);

            migrationBuilder.Sql(TriggerFunctions(", NULL, NULL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION queue_blob_deletion(text, text, text, uuid, text, text, text);
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlobDeletionCandidates_AdminIsComplete",
                table: "BlobDeletionCandidates");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeen",
                table: "BlobDeletionCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "BlobDeletionCandidates" SET "FirstSeen" = "QueuedAt" WHERE "Reason" = 'Admin';
                """);

            migrationBuilder.DropColumn(
                name: "QueuedAt",
                table: "BlobDeletionCandidates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlobDeletionCandidates_AdminIsComplete",
                table: "BlobDeletionCandidates",
                sql: "\"Reason\" <> 'Admin' OR (\"ExpectedVersion\" IS NOT NULL AND \"RequestedBy\" IS NOT NULL AND \"FirstSeen\" IS NOT NULL)");

            migrationBuilder.Sql("""
                CREATE FUNCTION queue_blob_deletion(
                    p_container text, p_blob_name text, p_reason text, p_repo_id uuid, p_user_id text,
                    p_requested_by text, p_expected_version text, p_first_seen timestamp with time zone)
                RETURNS void AS $$
                    INSERT INTO "BlobDeletionCandidates"
                        ("Container", "BlobName", "Reason", "RepoId", "UserId", "RequestedBy", "ExpectedVersion", "FirstSeen", "Requeued")
                    VALUES (p_container, p_blob_name, p_reason, p_repo_id, p_user_id, p_requested_by, p_expected_version, p_first_seen, 0)
                    ON CONFLICT ("Container", "BlobName") DO UPDATE SET
                        "Reason" = EXCLUDED."Reason",
                        "RepoId" = EXCLUDED."RepoId",
                        "UserId" = EXCLUDED."UserId",
                        "RequestedBy" = EXCLUDED."RequestedBy",
                        "ExpectedVersion" = EXCLUDED."ExpectedVersion",
                        "FirstSeen" = EXCLUDED."FirstSeen",
                        "Requeued" = "BlobDeletionCandidates"."Requeued" + 1;
                $$ LANGUAGE sql;
                """);

            migrationBuilder.Sql(TriggerFunctions(", NULL, NULL, NULL"));
        }


        /// <param name="trailingNulls">The arguments after the user id, which differ between the two versions of queue_blob_deletion.</param>
        private static string TriggerFunctions(string trailingNulls)
        {
            return $$"""
                CREATE OR REPLACE FUNCTION queue_mod_version_blob() RETURNS trigger AS $$
                BEGIN
                    PERFORM queue_blob_deletion(
                        'Mods', OLD."RepoId"::text || '/' || OLD."ModId" || '/' || OLD."Id",
                        'ReferenceRemoved', OLD."RepoId", NULL{{trailingNulls}});
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION queue_savegame_snapshot_blob() RETURNS trigger AS $$
                BEGIN
                    PERFORM queue_blob_deletion(
                        'Savegames', OLD."RepoId"::text || '/' || OLD."SavegameId"::text || '/' || OLD."ContentHash",
                        'ReferenceRemoved', OLD."RepoId", NULL{{trailingNulls}});
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION queue_mod_image_blob() RETURNS trigger AS $$
                BEGIN
                    PERFORM queue_blob_deletion(
                        'Images', left(OLD."Hash", 2) || '/' || OLD."Hash",
                        'ReferenceRemoved', OLD."ModVersionRepoId", NULL{{trailingNulls}});
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION queue_avatar_blob() RETURNS trigger AS $$
                BEGIN
                    IF OLD."AvatarHash" IS NOT NULL AND (TG_OP = 'DELETE' OR NEW."AvatarHash" IS DISTINCT FROM OLD."AvatarHash") THEN
                        PERFORM queue_blob_deletion(
                            'Images', left(OLD."AvatarHash", 2) || '/' || OLD."AvatarHash",
                            'ReferenceRemoved', NULL, OLD."Id"{{trailingNulls}});
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """;
        }
    }
}
