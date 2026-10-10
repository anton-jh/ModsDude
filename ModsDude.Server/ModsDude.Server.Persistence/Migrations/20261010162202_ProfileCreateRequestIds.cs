using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileCreateRequestIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreateRequestId",
                table: "Profiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // A distinct value per existing row, so the unique index below holds.
            migrationBuilder.Sql("UPDATE \"Profiles\" SET \"CreateRequestId\" = gen_random_uuid();");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_RepoId_CreateRequestId",
                table: "Profiles",
                columns: new[] { "RepoId", "CreateRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Profiles_RepoId_CreateRequestId",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "CreateRequestId",
                table: "Profiles");
        }
    }
}
