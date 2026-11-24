using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlobDeletionQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Generation",
                table: "StorageBindings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "PendingGeneration",
                table: "StorageBindings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageAccount",
                table: "StorageBindings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlobDeletionCandidates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Container = table.Column<string>(type: "text", nullable: false),
                    BlobName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpectedVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FirstSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Requeued = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlobDeletionCandidates", x => x.Id);
                    table.CheckConstraint("CK_BlobDeletionCandidates_AdminIsComplete", "\"Reason\" <> 'Admin' OR (\"ExpectedVersion\" IS NOT NULL AND \"RequestedBy\" IS NOT NULL AND \"FirstSeen\" IS NOT NULL)");
                    table.CheckConstraint("CK_BlobDeletionCandidates_Container", "\"Container\" IN ('Images', 'Mods', 'Savegames')");
                    table.CheckConstraint("CK_BlobDeletionCandidates_Reason", "\"Reason\" IN ('Admin', 'ReferenceRemoved', 'UploadIssued')");
                });

            migrationBuilder.CreateTable(
                name: "DeletedBlobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Container = table.Column<string>(type: "text", nullable: false),
                    BlobName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Deleted = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageAuditFindings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Container = table.Column<string>(type: "text", nullable: false),
                    BlobName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageAuditFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageMaintenanceStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastRun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAudit = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Refusals = table.Column<List<string>>(type: "text[]", nullable: false),
                    DeletedInLastRun = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageMaintenanceStatuses", x => x.Id);
                    table.CheckConstraint("CK_StorageMaintenanceStatus_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorageBindings_GenerationNotNegative",
                table: "StorageBindings",
                sql: "\"Generation\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorageBindings_PendingIsNext",
                table: "StorageBindings",
                sql: "\"PendingGeneration\" IS NULL OR \"PendingGeneration\" = \"Generation\" + 1");

            migrationBuilder.CreateIndex(
                name: "IX_BlobDeletionCandidates_Container_BlobName",
                table: "BlobDeletionCandidates",
                columns: new[] { "Container", "BlobName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeletedBlobs_Deleted",
                table: "DeletedBlobs",
                column: "Deleted");

            migrationBuilder.CreateIndex(
                name: "IX_StorageAuditFindings_Kind_Container_BlobName",
                table: "StorageAuditFindings",
                columns: new[] { "Kind", "Container", "BlobName" },
                unique: true);

            // One row per blob, through one function, for triggers and the API alike. Queuing a blob
            // that is queued already resets when its delay starts and counts the requeue, so a deletion
            // run that read the row before never drops it. Each trigger queues the blob a row referred
            // to when the row is deleted, by any statement and by any cascade, or stops referring to it.
            // The names follow BlobNames; the persistence tests hold the two together.
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

                CREATE FUNCTION queue_mod_version_blob() RETURNS trigger AS $$
                BEGIN
                    PERFORM queue_blob_deletion(
                        'Mods', OLD."RepoId"::text || '/' || OLD."ModId" || '/' || OLD."Id",
                        'ReferenceRemoved', OLD."RepoId", NULL, NULL, NULL, NULL);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER queue_blob_deletion
                    AFTER DELETE ON "ModVersions"
                    FOR EACH ROW EXECUTE FUNCTION queue_mod_version_blob();

                CREATE FUNCTION queue_savegame_snapshot_blob() RETURNS trigger AS $$
                BEGIN
                    PERFORM queue_blob_deletion(
                        'Savegames', OLD."RepoId"::text || '/' || OLD."SavegameId"::text || '/' || OLD."ContentHash",
                        'ReferenceRemoved', OLD."RepoId", NULL, NULL, NULL, NULL);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER queue_blob_deletion
                    AFTER DELETE ON "SavegameSnapshots"
                    FOR EACH ROW EXECUTE FUNCTION queue_savegame_snapshot_blob();

                CREATE FUNCTION queue_mod_image_blob() RETURNS trigger AS $$
                BEGIN
                    PERFORM queue_blob_deletion(
                        'Images', left(OLD."Hash", 2) || '/' || OLD."Hash",
                        'ReferenceRemoved', OLD."ModVersionRepoId", NULL, NULL, NULL, NULL);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER queue_blob_deletion
                    AFTER DELETE ON "ModImageReference"
                    FOR EACH ROW EXECUTE FUNCTION queue_mod_image_blob();

                CREATE FUNCTION queue_avatar_blob() RETURNS trigger AS $$
                BEGIN
                    IF OLD."AvatarHash" IS NOT NULL AND (TG_OP = 'DELETE' OR NEW."AvatarHash" IS DISTINCT FROM OLD."AvatarHash") THEN
                        PERFORM queue_blob_deletion(
                            'Images', left(OLD."AvatarHash", 2) || '/' || OLD."AvatarHash",
                            'ReferenceRemoved', NULL, OLD."Id", NULL, NULL, NULL);
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER queue_blob_deletion
                    AFTER UPDATE OF "AvatarHash" OR DELETE ON "Users"
                    FOR EACH ROW EXECUTE FUNCTION queue_avatar_blob();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER queue_blob_deletion ON "Users";
                DROP FUNCTION queue_avatar_blob();
                DROP TRIGGER queue_blob_deletion ON "ModImageReference";
                DROP FUNCTION queue_mod_image_blob();
                DROP TRIGGER queue_blob_deletion ON "SavegameSnapshots";
                DROP FUNCTION queue_savegame_snapshot_blob();
                DROP TRIGGER queue_blob_deletion ON "ModVersions";
                DROP FUNCTION queue_mod_version_blob();
                DROP FUNCTION queue_blob_deletion(text, text, text, uuid, text, text, text, timestamp with time zone);
                """);

            migrationBuilder.DropTable(
                name: "BlobDeletionCandidates");

            migrationBuilder.DropTable(
                name: "DeletedBlobs");

            migrationBuilder.DropTable(
                name: "StorageAuditFindings");

            migrationBuilder.DropTable(
                name: "StorageMaintenanceStatuses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StorageBindings_GenerationNotNegative",
                table: "StorageBindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StorageBindings_PendingIsNext",
                table: "StorageBindings");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "StorageBindings");

            migrationBuilder.DropColumn(
                name: "PendingGeneration",
                table: "StorageBindings");

            migrationBuilder.DropColumn(
                name: "StorageAccount",
                table: "StorageBindings");
        }
    }
}
