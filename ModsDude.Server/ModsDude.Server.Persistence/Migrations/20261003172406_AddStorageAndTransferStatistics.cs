using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageAndTransferStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    File = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTransfers", x => x.Id);
                    table.CheckConstraint("CK_FileTransfers_SizeNotNegative", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_FileTransfers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorageUsageSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Container = table.Column<string>(type: "text", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoredBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobCount = table.Column<int>(type: "integer", nullable: false),
                    RegisteredBytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageUsageSamples", x => x.Id);
                    table.CheckConstraint("CK_StorageUsageSamples_RegisteredNotNegative", "\"RegisteredBytes\" IS NULL OR \"RegisteredBytes\" >= 0");
                    table.CheckConstraint("CK_StorageUsageSamples_StoredNotNegative", "\"StoredBytes\" >= 0 AND \"BlobCount\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_At",
                table: "FileTransfers",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_RepoId_At",
                table: "FileTransfers",
                columns: new[] { "RepoId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_UserId_At",
                table: "FileTransfers",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageUsageSamples_Date_Container_RepoId",
                table: "StorageUsageSamples",
                columns: new[] { "Date", "Container", "RepoId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileTransfers");

            migrationBuilder.DropTable(
                name: "StorageUsageSamples");
        }
    }
}
