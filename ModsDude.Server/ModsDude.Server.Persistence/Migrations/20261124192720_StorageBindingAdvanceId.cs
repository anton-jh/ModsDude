using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StorageBindingAdvanceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceId",
                table: "StorageBindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingAdvanceId",
                table: "StorageBindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorageBindings_PendingHasAdvanceId",
                table: "StorageBindings",
                sql: "(\"PendingGeneration\" IS NULL) = (\"PendingAdvanceId\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StorageBindings_PendingHasAdvanceId",
                table: "StorageBindings");

            migrationBuilder.DropColumn(
                name: "AdvanceId",
                table: "StorageBindings");

            migrationBuilder.DropColumn(
                name: "PendingAdvanceId",
                table: "StorageBindings");
        }
    }
}
