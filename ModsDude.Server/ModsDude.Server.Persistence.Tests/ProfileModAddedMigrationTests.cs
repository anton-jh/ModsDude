using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Persistence.DbContexts;
using Npgsql;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The backfill that gave every existing dependency its date. It is SQL over history, so the only
/// honest test is to build history before the column existed and migrate across it.
/// </summary>
/// <remarks>
/// On a database of its own, outside the shared fixture: it has to stop the schema one migration short,
/// which the fixture - migrated to the latest for every other suite - cannot be made to do.
/// </remarks>
public class ProfileModAddedMigrationTests : IAsyncLifetime
{
    private const string _before = "20260920102309_ProfileIgnoredMods";
    private const string _target = "20260921092954_ProfileModAdded";

    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();

    private string _connectionString = null!;
    private ServiceProvider _services = null!;


    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseFixture.ConnectionString)
        {
            Database = "modsdude-tests-migration"
        };

        _connectionString = builder.ConnectionString;

        _services = new ServiceCollection()
            .AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(_connectionString))
            .BuildServiceProvider();

        using var dbContext = CreateDbContext();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.GetService<IMigrator>().MigrateAsync(_before);
    }

    public async Task DisposeAsync()
    {
        using var dbContext = CreateDbContext();

        await dbContext.Database.EnsureDeletedAsync();

        await _services.DisposeAsync();
    }


    [Fact]
    public async Task A_mods_date_is_the_first_revision_of_its_unbroken_run_at_one_version()
    {
        // Revisions 1 and 3-6 exist, 2 having been pruned. "keeps" is at 1.0 throughout; "moves"
        // changes version at 3; "leaves" is absent from revision 3 and returns at 4, so its run
        // starts again there; "survives" is at 1.0 in revisions 1 and 3, and the pruned revision
        // between them breaks nothing.
        await SeedAsync(
            revisions: [1, 3, 4, 5, 6],
            pins:
            [
                ("keeps", 1, "1.0"), ("keeps", 3, "1.0"), ("keeps", 4, "1.0"), ("keeps", 5, "1.0"), ("keeps", 6, "1.0"),
                ("moves", 1, "1.0"), ("moves", 3, "2.0"), ("moves", 4, "2.0"), ("moves", 5, "2.0"), ("moves", 6, "2.0"),
                ("leaves", 1, "1.0"), ("leaves", 4, "1.0"), ("leaves", 5, "1.0"), ("leaves", 6, "1.0"),
                ("survives", 1, "1.0"), ("survives", 3, "1.0")
            ]);

        using (var dbContext = CreateDbContext())
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(_target);
        }

        var added = await ReadAddedAsync();

        Assert.Equal(Day(1), added[("keeps", 6)]);
        Assert.Equal(Day(1), added[("keeps", 1)]);
        Assert.Equal(Day(1), added[("moves", 1)]);
        Assert.Equal(Day(3), added[("moves", 3)]);
        Assert.Equal(Day(3), added[("moves", 6)]);
        Assert.Equal(Day(4), added[("leaves", 4)]);
        Assert.Equal(Day(4), added[("leaves", 6)]);
        Assert.Equal(Day(1), added[("survives", 3)]);
    }


    /// <summary>Revision <paramref name="number"/> was made on this day, which is what its dependencies are dated.</summary>
    private static DateTime Day(int number) => new(2026, 1, number, 0, 0, 0, DateTimeKind.Utc);

    private ApplicationDbContext CreateDbContext() => _services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

    /// <summary>
    /// Written as raw rows with foreign keys switched off for the session: the tables the rows would
    /// otherwise need - a repo, a user, a version, a profile - have nothing to say about the query, and
    /// building them would bury it.
    /// </summary>
    private async Task SeedAsync(int[] revisions, (string Mod, int Revision, string Version)[] pins)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "SET session_replication_role = replica;");

        foreach (var revision in revisions)
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO "ProfileRevisions"
                    ("RepoId", "ProfileId", "Number", "ModCount", "CreatedBy", "Created", "Origin",
                     "Changes_Added", "Changes_Changed", "Changes_Removed")
                VALUES (@repo, @profile, @number, 0, 'author', @created, 'Saved', 0, 0, 0);
                """,
                ("repo", _repoId), ("profile", _profileId), ("number", revision), ("created", Day(revision)));
        }

        foreach (var (mod, revision, version) in pins)
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO "ModDependency" ("RepoId", "ProfileId", "RevisionNumber", "ModId", "ModVersionId", "Locked")
                VALUES (@repo, @profile, @revision, @mod, @version, false);
                """,
                ("repo", _repoId), ("profile", _profileId), ("revision", revision), ("mod", mod), ("version", version));
        }
    }

    private async Task<Dictionary<(string Mod, int Revision), DateTime>> ReadAddedAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""SELECT "ModId", "RevisionNumber", "Added" FROM "ModDependency";""", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var result = new Dictionary<(string, int), DateTime>();

        while (await reader.ReadAsync())
        {
            result[(reader.GetString(0), reader.GetInt32(1))] = reader.GetDateTime(2);
        }

        return result;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
