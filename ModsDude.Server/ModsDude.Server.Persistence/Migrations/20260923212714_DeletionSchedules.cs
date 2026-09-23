using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeletionSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "SavegameSnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeletionScheduledFor",
                table: "SavegameSnapshots",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "ProfileRevisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeletionScheduledFor",
                table: "ProfileRevisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "ModVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeletionScheduledFor",
                table: "ModVersions",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SavegameSnapshots_DeletionIsScheduledWithAReason",
                table: "SavegameSnapshots",
                sql: "(\"DeletionScheduledFor\" IS NULL) = (\"DeletionReason\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProfileRevisions_DeletionIsScheduledWithAReason",
                table: "ProfileRevisions",
                sql: "(\"DeletionScheduledFor\" IS NULL) = (\"DeletionReason\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModVersions_DeletionIsScheduledWithAReason",
                table: "ModVersions",
                sql: "(\"DeletionScheduledFor\" IS NULL) = (\"DeletionReason\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SavegameSnapshots_DeletionIsScheduledWithAReason",
                table: "SavegameSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProfileRevisions_DeletionIsScheduledWithAReason",
                table: "ProfileRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModVersions_DeletionIsScheduledWithAReason",
                table: "ModVersions");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "SavegameSnapshots");

            migrationBuilder.DropColumn(
                name: "DeletionScheduledFor",
                table: "SavegameSnapshots");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "ProfileRevisions");

            migrationBuilder.DropColumn(
                name: "DeletionScheduledFor",
                table: "ProfileRevisions");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "ModVersions");

            migrationBuilder.DropColumn(
                name: "DeletionScheduledFor",
                table: "ModVersions");
        }
    }
}
