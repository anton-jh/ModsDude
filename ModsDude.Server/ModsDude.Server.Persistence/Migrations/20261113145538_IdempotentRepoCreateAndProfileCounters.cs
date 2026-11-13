using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotentRepoCreateAndProfileCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepoCreateRequests",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoCreateRequests", x => new { x.UserId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_RepoCreateRequests_Repos_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepoCreateRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepoCreateRequests_RepoId",
                table: "RepoCreateRequests",
                column: "RepoId");

            // A profile's history and its ignore list are what a client shows of it beside the
            // profile row itself, and retention changes both without touching that row.
            migrationBuilder.Sql("""
                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "ProfileRevisions"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Profiles', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "ProfileIgnoredMods"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Profiles', 'RepoId');

                CREATE TRIGGER count_repo_change
                    AFTER INSERT OR UPDATE OR DELETE ON "ProfileDismissedVersions"
                    FOR EACH ROW EXECUTE FUNCTION count_repo_change('Profiles', 'RepoId');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER count_repo_change ON "ProfileDismissedVersions";
                DROP TRIGGER count_repo_change ON "ProfileIgnoredMods";
                DROP TRIGGER count_repo_change ON "ProfileRevisions";
                """);

            migrationBuilder.DropTable(
                name: "RepoCreateRequests");
        }
    }
}
