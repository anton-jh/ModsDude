using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModImageReferencesAndModListDelta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency");

            migrationBuilder.CreateIndex(
                name: "IX_ModVersions_RepoId_Updated_ModId_Id",
                table: "ModVersions",
                columns: new[] { "RepoId", "Updated", "ModId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ModId", "ModVersionId" },
                principalTable: "ModVersions",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency");

            migrationBuilder.DropIndex(
                name: "IX_ModVersions_RepoId_Updated_ModId_Id",
                table: "ModVersions");

            migrationBuilder.AddForeignKey(
                name: "FK_ModDependency_ModVersions_RepoId_ModId_ModVersionId",
                table: "ModDependency",
                columns: new[] { "RepoId", "ModId", "ModVersionId" },
                principalTable: "ModVersions",
                principalColumns: new[] { "RepoId", "ModId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
