using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequiredExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "TrustCodes",
                type: "timestamp with time zone",
                nullable: true);

            // Every existing code keeps the fixed 48 hours it was issued with.
            migrationBuilder.Sql("""UPDATE "TrustCodes" SET "ExpiresAt" = "Created" + interval '48 hours';""");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                table: "TrustCodes",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            // An invite without an expiry gets the longest one offered, counted from the deploy.
            migrationBuilder.Sql("""
                UPDATE "RepoInvites"
                SET "ExpiresAt" = GREATEST(now(), "Created") + interval '30 days'
                WHERE "ExpiresAt" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                table: "RepoInvites",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrustCodes_ExpiresAfterCreated",
                table: "TrustCodes",
                sql: "\"ExpiresAt\" > \"Created\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RepoInvites_ExpiresAfterCreated",
                table: "RepoInvites",
                sql: "\"ExpiresAt\" > \"Created\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TrustCodes_ExpiresAfterCreated",
                table: "TrustCodes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RepoInvites_ExpiresAfterCreated",
                table: "RepoInvites");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "TrustCodes");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                table: "RepoInvites",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }
    }
}
