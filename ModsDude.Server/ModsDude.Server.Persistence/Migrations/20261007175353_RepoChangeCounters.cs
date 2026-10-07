using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepoChangeCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Activity",
                table: "RepoChangeCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Members",
                table: "RepoChangeCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Profiles",
                table: "RepoChangeCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Repo",
                table: "RepoChangeCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Savegames",
                table: "RepoChangeCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // TG_ARGV[0] is the counter, TG_ARGV[1] the column naming the repo. A repo being deleted
            // takes its counter row with it, so the rows its cascade removes count nothing. The row is
            // created by whichever counter moves first, so every column needs a default.
            migrationBuilder.Sql("""
                ALTER TABLE "RepoChangeCounters" ALTER COLUMN "Mods" SET DEFAULT 0;

                CREATE FUNCTION count_repo_change() RETURNS trigger AS $$
                DECLARE
                    repo uuid := ((CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END) ->> TG_ARGV[1])::uuid;
                BEGIN
                    EXECUTE format(
                        'INSERT INTO "RepoChangeCounters" ("RepoId", %1$I)
                         SELECT $1, 1 WHERE EXISTS (SELECT 1 FROM "Repos" WHERE "Id" = $1)
                         ON CONFLICT ("RepoId") DO UPDATE SET %1$I = "RepoChangeCounters".%1$I + 1',
                        TG_ARGV[0])
                    USING repo;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE ON "Repos"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Repo', 'Id');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "Profiles"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Profiles', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "Savegames"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Savegames', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "SavegameSnapshots"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Savegames', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "SavegameCheckouts"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Savegames', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "RepoMemberships"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Members', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "GameActivities"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Activity', 'RepoId');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER count_repo_change ON "GameActivities";
                DROP TRIGGER count_repo_change ON "RepoMemberships";
                DROP TRIGGER count_repo_change ON "SavegameCheckouts";
                DROP TRIGGER count_repo_change ON "SavegameSnapshots";
                DROP TRIGGER count_repo_change ON "Savegames";
                DROP TRIGGER count_repo_change ON "Profiles";
                DROP TRIGGER count_repo_change ON "Repos";
                DROP FUNCTION count_repo_change();

                ALTER TABLE "RepoChangeCounters" ALTER COLUMN "Mods" DROP DEFAULT;
                """);

            migrationBuilder.DropColumn(
                name: "Activity",
                table: "RepoChangeCounters");

            migrationBuilder.DropColumn(
                name: "Members",
                table: "RepoChangeCounters");

            migrationBuilder.DropColumn(
                name: "Profiles",
                table: "RepoChangeCounters");

            migrationBuilder.DropColumn(
                name: "Repo",
                table: "RepoChangeCounters");

            migrationBuilder.DropColumn(
                name: "Savegames",
                table: "RepoChangeCounters");
        }
    }
}
