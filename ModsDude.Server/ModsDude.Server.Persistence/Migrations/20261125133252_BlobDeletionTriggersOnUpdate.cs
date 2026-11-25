using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlobDeletionTriggersOnUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A row that stops naming a blob by having its name columns changed releases it as surely as
            // one that is deleted, so the triggers fire on both and queue the name the row held before.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION queue_mod_version_blob() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR (NEW."RepoId", NEW."ModId", NEW."Id") IS DISTINCT FROM (OLD."RepoId", OLD."ModId", OLD."Id") THEN
                        PERFORM queue_blob_deletion(
                            'Mods', OLD."RepoId"::text || '/' || OLD."ModId" || '/' || OLD."Id",
                            'ReferenceRemoved', OLD."RepoId", NULL, NULL, NULL);
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION queue_savegame_snapshot_blob() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR (NEW."RepoId", NEW."SavegameId", NEW."ContentHash") IS DISTINCT FROM (OLD."RepoId", OLD."SavegameId", OLD."ContentHash") THEN
                        PERFORM queue_blob_deletion(
                            'Savegames', OLD."RepoId"::text || '/' || OLD."SavegameId"::text || '/' || OLD."ContentHash",
                            'ReferenceRemoved', OLD."RepoId", NULL, NULL, NULL);
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION queue_mod_image_blob() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR NEW."Hash" IS DISTINCT FROM OLD."Hash" THEN
                        PERFORM queue_blob_deletion(
                            'Images', left(OLD."Hash", 2) || '/' || OLD."Hash",
                            'ReferenceRemoved', OLD."ModVersionRepoId", NULL, NULL, NULL);
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER queue_blob_deletion ON "ModVersions";
                CREATE TRIGGER queue_blob_deletion
                    AFTER UPDATE OF "RepoId", "ModId", "Id" OR DELETE ON "ModVersions"
                    FOR EACH ROW EXECUTE FUNCTION queue_mod_version_blob();

                DROP TRIGGER queue_blob_deletion ON "SavegameSnapshots";
                CREATE TRIGGER queue_blob_deletion
                    AFTER UPDATE OF "RepoId", "SavegameId", "ContentHash" OR DELETE ON "SavegameSnapshots"
                    FOR EACH ROW EXECUTE FUNCTION queue_savegame_snapshot_blob();

                DROP TRIGGER queue_blob_deletion ON "ModImageReference";
                CREATE TRIGGER queue_blob_deletion
                    AFTER UPDATE OF "Hash" OR DELETE ON "ModImageReference"
                    FOR EACH ROW EXECUTE FUNCTION queue_mod_image_blob();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER queue_blob_deletion ON "ModVersions";
                CREATE TRIGGER queue_blob_deletion
                    AFTER DELETE ON "ModVersions"
                    FOR EACH ROW EXECUTE FUNCTION queue_mod_version_blob();

                DROP TRIGGER queue_blob_deletion ON "SavegameSnapshots";
                CREATE TRIGGER queue_blob_deletion
                    AFTER DELETE ON "SavegameSnapshots"
                    FOR EACH ROW EXECUTE FUNCTION queue_savegame_snapshot_blob();

                DROP TRIGGER queue_blob_deletion ON "ModImageReference";
                CREATE TRIGGER queue_blob_deletion
                    AFTER DELETE ON "ModImageReference"
                    FOR EACH ROW EXECUTE FUNCTION queue_mod_image_blob();
                """);
        }
    }
}
