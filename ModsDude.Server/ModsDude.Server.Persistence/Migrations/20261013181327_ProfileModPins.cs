using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileModPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency");

            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ProfileRevisions_RepoId_ProfileId_RevisionNum~",
                table: "ModDependency");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency");

            migrationBuilder.RenameTable(
                name: "ModDependency",
                newName: "ProfileModPin");

            migrationBuilder.RenameIndex(
                name: "IX_ModDependency_RepoId_ProfileId_RevisionNumber_ModId",
                table: "ProfileModPin",
                newName: "IX_ProfileModPin_RepoId_ProfileId_RevisionNumber_ModId");

            migrationBuilder.RenameIndex(
                name: "IX_ModDependency_RepoId_ModId_ModVersionId",
                table: "ProfileModPin",
                newName: "IX_ProfileModPin_RepoId_ModId_ModVersionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProfileModPin",
                table: "ProfileModPin",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber", "ModId", "ModVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileModPin_ModVersions_RepoId_ModId_ModVersionId",
                table: "ProfileModPin",
                columns: new[] { "RepoId", "ModId", "ModVersionId" },
                principalTable: "ModVersions",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileModPin_ProfileRevisions_RepoId_ProfileId_RevisionNum~",
                table: "ProfileModPin",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber" },
                principalTable: "ProfileRevisions",
                principalColumns: new[] { "RepoId", "ProfileId", "Number" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProfileModPin_ModVersions_RepoId_ModId_ModVersionId",
                table: "ProfileModPin");

            migrationBuilder.DropForeignKey(
                name: "FK_ProfileModPin_ProfileRevisions_RepoId_ProfileId_RevisionNum~",
                table: "ProfileModPin");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProfileModPin",
                table: "ProfileModPin");

            migrationBuilder.RenameTable(
                name: "ProfileModPin",
                newName: "ModDependency");

            migrationBuilder.RenameIndex(
                name: "IX_ProfileModPin_RepoId_ProfileId_RevisionNumber_ModId",
                table: "ModDependency",
                newName: "IX_ModDependency_RepoId_ProfileId_RevisionNumber_ModId");

            migrationBuilder.RenameIndex(
                name: "IX_ProfileModPin_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                newName: "IX_ModDependency_RepoId_ModId_ModVersionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModDependency",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber", "ModId", "ModVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ModId", "ModVersionId" },
                principalTable: "ModVersions",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ProfileRevisions_RepoId_ProfileId_RevisionNum~",
                table: "ModDependency",
                columns: new[] { "RepoId", "ProfileId", "RevisionNumber" },
                principalTable: "ProfileRevisions",
                principalColumns: new[] { "RepoId", "ProfileId", "Number" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
