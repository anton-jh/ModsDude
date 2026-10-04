using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepoInviteRequestIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepoInvites_RepoId",
                table: "RepoInvites");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "RepoInvites",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "RepoInvites",
                type: "uuid",
                nullable: true);

            // A distinct value per existing row, so the unique index below holds.
            migrationBuilder.Sql("UPDATE \"RepoInvites\" SET \"RequestId\" = gen_random_uuid();");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestId",
                table: "RepoInvites",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepoInvites_RepoId_RequestId",
                table: "RepoInvites",
                columns: new[] { "RepoId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepoInvites_RepoId_RequestId",
                table: "RepoInvites");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "RepoInvites");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "RepoInvites",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepoInvites_RepoId",
                table: "RepoInvites",
                column: "RepoId");
        }
    }
}
