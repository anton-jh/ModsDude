using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrustCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RedeemedBy = table.Column<string>(type: "text", nullable: true),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustCodes", x => x.Id);
                    table.CheckConstraint("CK_TrustCodes_NotRedeemedAndRevoked", "\"RedeemedAt\" IS NULL OR \"RevokedAt\" IS NULL");
                    table.CheckConstraint("CK_TrustCodes_RedeemedByAndAtTogether", "(\"RedeemedBy\" IS NULL) = (\"RedeemedAt\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_TrustCodes_Users_RedeemedBy",
                        column: x => x.RedeemedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustCodes_Code",
                table: "TrustCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustCodes_RedeemedBy",
                table: "TrustCodes",
                column: "RedeemedBy",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustCodes_RequestId",
                table: "TrustCodes",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustCodes");
        }
    }
}
