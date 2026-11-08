using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenStorageAndValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrustCodes_RedeemedBy",
                table: "TrustCodes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrustCodes_RedeemedByAndAtTogether",
                table: "TrustCodes");

            migrationBuilder.AddColumn<int>(
                name: "TrustEpoch",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RedeemedTrustEpoch",
                table: "TrustCodes",
                type: "integer",
                nullable: true);

            // Every code redeemed so far was redeemed in its user's first epoch.
            migrationBuilder.Sql("""UPDATE "TrustCodes" SET "RedeemedTrustEpoch" = 0 WHERE "RedeemedBy" IS NOT NULL;""");

            // Names are now stored trimmed and at most 100 characters long.
            migrationBuilder.Sql("""UPDATE "Repos" SET "Name" = left(btrim("Name"), 100) WHERE "Name" <> left(btrim("Name"), 100);""");
            migrationBuilder.Sql("""UPDATE "Profiles" SET "Name" = left(btrim("Name"), 100) WHERE "Name" <> left(btrim("Name"), 100);""");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Repos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionId",
                table: "ProfileModPin",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ProfileModPin",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ProfileIgnoredMods",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "VersionId",
                table: "ProfileDismissedVersions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ProfileDismissedVersions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "ModVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ModVersions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ModVersions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "VersionId",
                table: "ModVersionDeletions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ModVersionDeletions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionId",
                table: "ModImageReference",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionModId",
                table: "ModImageReference",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionId",
                table: "ModAttribute",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionModId",
                table: "ModAttribute",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "StorageBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageBindings", x => x.Id);
                    table.CheckConstraint("CK_StorageBindings_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustCodes_RedeemedBy_RedeemedTrustEpoch",
                table: "TrustCodes",
                columns: new[] { "RedeemedBy", "RedeemedTrustEpoch" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrustCodes_RedeemedByAndAtTogether",
                table: "TrustCodes",
                sql: "(\"RedeemedBy\" IS NULL) = (\"RedeemedAt\" IS NULL) AND (\"RedeemedBy\" IS NULL) = (\"RedeemedTrustEpoch\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RepoMemberships_Level",
                table: "RepoMemberships",
                sql: "\"Level\" IN ('Admin', 'Guest', 'Member')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RepoInvites_GrantedLevel",
                table: "RepoInvites",
                sql: "\"GrantedLevel\" IN ('Admin', 'Guest', 'Member')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModVersions_ContentHashIsSha256Hex",
                table: "ModVersions",
                sql: "\"ContentHash\" ~ '^[0-9a-f]{64}$'");

            migrationBuilder.CreateIndex(
                name: "IX_ModImageReference_Hash",
                table: "ModImageReference",
                column: "Hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageBindings");

            migrationBuilder.DropIndex(
                name: "IX_TrustCodes_RedeemedBy_RedeemedTrustEpoch",
                table: "TrustCodes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrustCodes_RedeemedByAndAtTogether",
                table: "TrustCodes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RepoMemberships_Level",
                table: "RepoMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RepoInvites_GrantedLevel",
                table: "RepoInvites");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModVersions_ContentHashIsSha256Hex",
                table: "ModVersions");

            migrationBuilder.DropIndex(
                name: "IX_ModImageReference_Hash",
                table: "ModImageReference");

            migrationBuilder.DropColumn(
                name: "TrustEpoch",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RedeemedTrustEpoch",
                table: "TrustCodes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Repos",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Profiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionId",
                table: "ProfileModPin",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ProfileModPin",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ProfileIgnoredMods",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "VersionId",
                table: "ProfileDismissedVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ProfileDismissedVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "ModVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ModVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ModVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "VersionId",
                table: "ModVersionDeletions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModId",
                table: "ModVersionDeletions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionId",
                table: "ModImageReference",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionModId",
                table: "ModImageReference",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionId",
                table: "ModAttribute",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ModVersionModId",
                table: "ModAttribute",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.CreateIndex(
                name: "IX_TrustCodes_RedeemedBy",
                table: "TrustCodes",
                column: "RedeemedBy",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrustCodes_RedeemedByAndAtTogether",
                table: "TrustCodes",
                sql: "(\"RedeemedBy\" IS NULL) = (\"RedeemedAt\" IS NULL)");
        }
    }
}
