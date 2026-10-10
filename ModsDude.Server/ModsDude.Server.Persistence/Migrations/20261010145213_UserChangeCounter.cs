using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModsDude.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserChangeCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChangeCount",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // BEFORE, so the count is part of the same row write: a reader never sees the new values
            // with the old count.
            migrationBuilder.Sql("""
                CREATE FUNCTION count_user_change() RETURNS trigger AS $$
                BEGIN
                    NEW."ChangeCount" := OLD."ChangeCount" + 1;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER count_user_change
                    BEFORE UPDATE ON "Users"
                    FOR EACH ROW
                    WHEN (OLD."DisplayName" IS DISTINCT FROM NEW."DisplayName"
                        OR OLD."AvatarHash" IS DISTINCT FROM NEW."AvatarHash"
                        OR OLD."IsTrusted" IS DISTINCT FROM NEW."IsTrusted")
                    EXECUTE FUNCTION count_user_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER count_user_change ON "Users";
                DROP FUNCTION count_user_change();
                """);

            migrationBuilder.DropColumn(
                name: "ChangeCount",
                table: "Users");
        }
    }
}
