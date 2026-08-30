using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModImageRendition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added nullable and tightened after the backfill, rather than taking a default: every
            // existing row already says which rendition it is, just in the wrong field, so there is
            // no row a default would be right for.
            migrationBuilder.AddColumn<string>(
                name: "Rendition",
                table: "ModImageReference",
                type: "text",
                nullable: true);

            // A store image used to encode its rendition in Position — index * 2 for the full, one
            // past it for the thumbnail — because there was no field to carry it. Decode that, and
            // give Position back its plain meaning. An icon had no full rendition at all, so the one
            // reference it has is its thumbnail and its position is already an index.
            migrationBuilder.Sql("""
                UPDATE "ModImageReference"
                SET "Rendition" = CASE WHEN "Kind" = 'StoreImage' AND "Position" % 2 = 0 THEN 'Full' ELSE 'Thumbnail' END,
                    "Position" = CASE WHEN "Kind" = 'StoreImage' THEN "Position" / 2 ELSE "Position" END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Rendition",
                table: "ModImageReference",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ModImageReference"
                SET "Position" = ("Position" * 2) + CASE WHEN "Rendition" = 'Thumbnail' THEN 1 ELSE 0 END
                WHERE "Kind" = 'StoreImage';
                """);

            // The old model has room for one icon and only as a thumbnail, so its full rendition has
            // nowhere to go back to.
            migrationBuilder.Sql("""
                DELETE FROM "ModImageReference"
                WHERE "Kind" = 'Icon' AND "Rendition" <> 'Thumbnail';
                """);

            migrationBuilder.DropColumn(
                name: "Rendition",
                table: "ModImageReference");
        }
    }
}
