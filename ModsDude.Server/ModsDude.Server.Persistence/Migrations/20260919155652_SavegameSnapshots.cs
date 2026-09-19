using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavegameSnapshots : Migration
    {
        // A rename in place. The scaffolded migration dropped and recreated both tables, which would
        // have taken every savegame's history with it; nothing about the data changes, only what it
        // is called. The constraint names are renamed as well, so that a database that took this
        // migration is indistinguishable from one built fresh from the model.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "HeadVersion",
                table: "Savegames",
                newName: "HeadSnapshot");

            migrationBuilder.RenameTable(
                name: "SavegameVersions",
                newName: "SavegameSnapshots");

            migrationBuilder.RenameTable(
                name: "SavegameVersionDetails",
                newName: "SavegameSnapshotDetails");

            migrationBuilder.RenameColumn(
                name: "BaseVersion",
                table: "SavegameSnapshots",
                newName: "BaseSnapshot");

            migrationBuilder.RenameColumn(
                name: "SavegameVersionRepoId",
                table: "SavegameSnapshotDetails",
                newName: "SavegameSnapshotRepoId");

            migrationBuilder.RenameColumn(
                name: "SavegameVersionSavegameId",
                table: "SavegameSnapshotDetails",
                newName: "SavegameSnapshotSavegameId");

            migrationBuilder.RenameColumn(
                name: "SavegameVersionNumber",
                table: "SavegameSnapshotDetails",
                newName: "SavegameSnapshotNumber");

            migrationBuilder.RenameIndex(
                name: "IX_SavegameVersions_RepoId_ProfileId_ProfileRevision",
                table: "SavegameSnapshots",
                newName: "IX_SavegameSnapshots_RepoId_ProfileId_ProfileRevision");

            migrationBuilder.RenameIndex(
                name: "IX_SavegameVersions_RepoId_SavegameId_ContentHash",
                table: "SavegameSnapshots",
                newName: "IX_SavegameSnapshots_RepoId_SavegameId_ContentHash");

            RenameConstraint(migrationBuilder, "SavegameSnapshots", "PK_SavegameVersions", "PK_SavegameSnapshots");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "CK_SavegameVersions_ProfileAndRevisionAreSetTogether", "CK_SavegameSnapshots_ProfileAndRevisionAreSetTogether");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "FK_SavegameVersions_ProfileRevisions_RepoId_ProfileId_ProfileR~", "FK_SavegameSnapshots_ProfileRevisions_RepoId_ProfileId_Profile~");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "FK_SavegameVersions_Savegames_RepoId_SavegameId", "FK_SavegameSnapshots_Savegames_RepoId_SavegameId");
            RenameConstraint(migrationBuilder, "SavegameSnapshotDetails", "PK_SavegameVersionDetails", "PK_SavegameSnapshotDetails");
            RenameConstraint(migrationBuilder, "SavegameSnapshotDetails", "FK_SavegameVersionDetails_SavegameVersions_SavegameVersionRepo~", "FK_SavegameSnapshotDetails_SavegameSnapshots_SavegameSnapshotR~");

            migrationBuilder.Sql("ALTER SEQUENCE \"SavegameVersionDetails_Id_seq\" RENAME TO \"SavegameSnapshotDetails_Id_seq\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SEQUENCE \"SavegameSnapshotDetails_Id_seq\" RENAME TO \"SavegameVersionDetails_Id_seq\";");

            RenameConstraint(migrationBuilder, "SavegameSnapshotDetails", "FK_SavegameSnapshotDetails_SavegameSnapshots_SavegameSnapshotR~", "FK_SavegameVersionDetails_SavegameVersions_SavegameVersionRepo~");
            RenameConstraint(migrationBuilder, "SavegameSnapshotDetails", "PK_SavegameSnapshotDetails", "PK_SavegameVersionDetails");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "FK_SavegameSnapshots_Savegames_RepoId_SavegameId", "FK_SavegameVersions_Savegames_RepoId_SavegameId");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "FK_SavegameSnapshots_ProfileRevisions_RepoId_ProfileId_Profile~", "FK_SavegameVersions_ProfileRevisions_RepoId_ProfileId_ProfileR~");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "CK_SavegameSnapshots_ProfileAndRevisionAreSetTogether", "CK_SavegameVersions_ProfileAndRevisionAreSetTogether");
            RenameConstraint(migrationBuilder, "SavegameSnapshots", "PK_SavegameSnapshots", "PK_SavegameVersions");

            migrationBuilder.RenameIndex(
                name: "IX_SavegameSnapshots_RepoId_SavegameId_ContentHash",
                table: "SavegameSnapshots",
                newName: "IX_SavegameVersions_RepoId_SavegameId_ContentHash");

            migrationBuilder.RenameIndex(
                name: "IX_SavegameSnapshots_RepoId_ProfileId_ProfileRevision",
                table: "SavegameSnapshots",
                newName: "IX_SavegameVersions_RepoId_ProfileId_ProfileRevision");

            migrationBuilder.RenameColumn(
                name: "SavegameSnapshotNumber",
                table: "SavegameSnapshotDetails",
                newName: "SavegameVersionNumber");

            migrationBuilder.RenameColumn(
                name: "SavegameSnapshotSavegameId",
                table: "SavegameSnapshotDetails",
                newName: "SavegameVersionSavegameId");

            migrationBuilder.RenameColumn(
                name: "SavegameSnapshotRepoId",
                table: "SavegameSnapshotDetails",
                newName: "SavegameVersionRepoId");

            migrationBuilder.RenameColumn(
                name: "BaseSnapshot",
                table: "SavegameSnapshots",
                newName: "BaseVersion");

            migrationBuilder.RenameTable(
                name: "SavegameSnapshotDetails",
                newName: "SavegameVersionDetails");

            migrationBuilder.RenameTable(
                name: "SavegameSnapshots",
                newName: "SavegameVersions");

            migrationBuilder.RenameColumn(
                name: "HeadSnapshot",
                table: "Savegames",
                newName: "HeadVersion");
        }

        private static void RenameConstraint(MigrationBuilder migrationBuilder, string table, string from, string to)
        {
            migrationBuilder.Sql($"ALTER TABLE \"{table}\" RENAME CONSTRAINT \"{from}\" TO \"{to}\";");
        }
    }
}
