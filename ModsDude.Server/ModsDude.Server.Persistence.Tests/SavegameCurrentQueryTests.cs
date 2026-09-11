using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using Npgsql;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The rule that makes "current" mean anything: at most one current savegame per profile, enforced
/// by a filtered unique index rather than by whoever wrote the endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The point of the whole design is that forgetting produces the safe outcome. A profile that
/// quietly acquired two current savegames would put both of them back to following the mod list, which
/// is the state this exists to prevent - so the database has to be the one saying no, and only a
/// real PostgreSQL can answer for a partial index and the null semantics underneath it.
/// </para>
/// <para>
/// Three things are checked here that a migration can silently drop: the filter clause, its
/// <em>absence</em> on <c>ArchivedAt</c>, and the two check constraints that keep a half-set profile
/// pair out of the tables.
/// </para>
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SavegameCurrentQueryTests(DatabaseFixture fixture)
{
    private static readonly UserId _author = new("author");
    private static readonly DateTime _supersededAt = new(2026, 4, 1, 20, 0, 0, DateTimeKind.Utc);


    /// <summary>
    /// The most important test in the file. Two publishes to one profile, both reading no current
    /// savegame, and exactly one commits - which is the only reason an endpoint may treat the
    /// unsuperseded row as the current savegame instead of locking the profile.
    /// </summary>
    [Fact]
    public async Task Two_current_savegames_on_one_profile_are_refused_by_the_database()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();

        await GivenACurrentSavegame(repoId, profileId);

        using var dbContext = fixture.CreateDbContext();

        dbContext.Savegames.Add(new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// The other half of the same rule. The index has to constrain unsuperseded rows only, or a
    /// profile could carry exactly one savegame in its life and starting a second would be impossible.
    /// </summary>
    [Fact]
    public async Task A_past_savegame_does_not_stand_in_the_way_of_a_new_one()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();

        var first = await GivenACurrentSavegame(repoId, profileId);
        var second = await GivenAPublishThatSupersedes(repoId, profileId);

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None);

        Assert.Equal(_supersededAt, rows.Single(x => x.Id == first).SupersededAt);
        Assert.Null(rows.Single(x => x.Id == second).SupersededAt);
    }

    /// <summary>
    /// A profile that has been played on for years accumulates one past savegame per publish, and none
    /// of them is ever pruned. Nothing about the index may make that history cost anything.
    /// </summary>
    [Fact]
    public async Task A_profile_may_carry_any_number_of_past_savegames()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();

        await GivenACurrentSavegame(repoId, profileId);

        foreach (var _ in Enumerable.Range(0, 4))
        {
            await GivenAPublishThatSupersedes(repoId, profileId);
        }

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None);

        Assert.Equal(5, rows.Count);
        Assert.Single(rows, x => x.SupersededAt is null);
    }

    /// <summary>
    /// <b>Archiving does not hand the profile's slot back.</b> The index is deliberately not filtered
    /// on <c>ArchivedAt</c>, unlike the savegame-name index beside it: archived is the repo-wide
    /// visibility state and past is which savegame a profile follows, and copying the filter across would
    /// let a second savegame become current behind an archived one. A profile whose current savegame is
    /// archived still has a current savegame, which is a sentence the interface has to be able to say.
    /// </summary>
    [Fact]
    public async Task An_archived_savegame_still_holds_its_profiles_current_slot()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();

        var savegameId = await GivenACurrentSavegame(repoId, profileId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            savegame.Archive(DateTime.UtcNow);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Savegames.Add(new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow));

            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            // And it is still what the read finds, so the publish that does supersede it finds
            // something to supersede rather than sailing past into the index.
            var current = await dbContext.Savegames.GetCurrentAsync(repoId, profileId, CancellationToken.None);

            Assert.Equal(savegameId, current?.Id);
        }
    }

    /// <summary>
    /// Savegames with no mod list are unconstrained by the index, and that is the null semantics of a
    /// unique index doing it rather than a second code path: PostgreSQL treats nulls as distinct, so
    /// any number of them coexist in one repo. A repo whose adapter has no mod support is entirely
    /// made of these.
    /// </summary>
    [Fact]
    public async Task Any_number_of_savegames_may_follow_no_profile_at_all()
    {
        var (repoId, _) = await GivenARepoWithAProfile();

        using var dbContext = fixture.CreateDbContext();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            dbContext.Savegames.Add(new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), null, DateTime.UtcNow));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rows = await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, x => Assert.Null(x.ProfileId));
        Assert.All(rows, x => Assert.Null(x.SupersededAt));
    }

    /// <summary>
    /// The swap, in the order it has to happen. Superseding the incumbent first is what gets a
    /// profile from one current savegame to another without the index seeing two, and it is the reason
    /// the endpoints write two commits inside one transaction rather than one <c>SaveChanges</c>.
    /// </summary>
    [Fact]
    public async Task Making_a_past_savegame_current_again_needs_the_incumbent_out_of_the_way_first()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();

        var first = await GivenACurrentSavegame(repoId, profileId);
        var second = await GivenAPublishThatSupersedes(repoId, profileId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var past = (await dbContext.Savegames.GetAsync(repoId, first, CancellationToken.None))!;

            past.MakeCurrent();

            // Nothing has vacated the slot, so this is the intermediate state the index exists to
            // refuse - and it refuses it whether or not the caller meant to leave it there.
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            var incumbent = (await dbContext.Savegames.GetAsync(repoId, second, CancellationToken.None))!;

            incumbent.Supersede(_supersededAt);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var past = (await dbContext.Savegames.GetAsync(repoId, first, CancellationToken.None))!;

            past.MakeCurrent();
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            var current = await dbContext.Savegames.GetCurrentAsync(repoId, profileId, CancellationToken.None);

            Assert.Equal(first, current?.Id);
        }
    }

    /// <summary>
    /// Superseded is a sentence about a profile, so a savegame that follows none cannot be in that
    /// state. The domain refuses it too; this is the half that holds when somebody writes SQL.
    /// </summary>
    [Fact]
    public async Task A_savegame_with_no_profile_cannot_be_superseded()
    {
        var (repoId, _) = await GivenARepoWithAProfile();

        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), null, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Written as SQL because the domain will not produce the row - which is the point: this is
        // the half of the rule that holds when the next writer is a migration or a psql session.
        await Assert.ThrowsAsync<PostgresException>(() => dbContext.Database.ExecuteSqlRawAsync(
            """UPDATE "Savegames" SET "SupersededAt" = {0} WHERE "Id" = {1}""",
            _supersededAt, savegame.Id.Value));
    }

    /// <summary>
    /// The pairing on a version, from the side the domain cannot see: a profile with no revision.
    /// A revision is only readable against the profile that issued it, so half a pair is a row that
    /// means nothing rather than a row that is merely incomplete.
    /// </summary>
    [Fact]
    public async Task A_version_cannot_name_a_profile_without_a_revision()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenACurrentSavegame(repoId, profileId);

        await GivenAVersion(repoId, savegameId);

        using var dbContext = fixture.CreateDbContext();

        await Assert.ThrowsAsync<PostgresException>(() => dbContext.Database.ExecuteSqlRawAsync(
            """UPDATE "SavegameVersions" SET "ProfileRevision" = NULL WHERE "SavegameId" = {0}""",
            savegameId.Value));
    }

    /// <inheritdoc cref="A_version_cannot_name_a_profile_without_a_revision"/>
    [Fact]
    public async Task A_version_cannot_name_a_revision_without_a_profile()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenACurrentSavegame(repoId, profileId);

        await GivenAVersion(repoId, savegameId);

        using var dbContext = fixture.CreateDbContext();

        await Assert.ThrowsAsync<PostgresException>(() => dbContext.Database.ExecuteSqlRawAsync(
            """UPDATE "SavegameVersions" SET "ProfileId" = NULL WHERE "SavegameId" = {0}""",
            savegameId.Value));
    }

    /// <summary>
    /// The state the nullability exists for, written all the way to the database: a savegame with no
    /// mod list, and a version of it naming neither profile nor revision. The foreign key onto the
    /// revision has a null in it and is therefore not checked, which is what lets these rows exist
    /// without a nullable-aware path anywhere above them.
    /// </summary>
    [Fact]
    public async Task A_version_may_name_neither_a_profile_nor_a_revision()
    {
        var (repoId, _) = await GivenARepoWithAProfile();

        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), null, DateTime.UtcNow);
        var version = savegame.CreateVersion(
            null, new string('4', ModImageHash.Length), 1024, _author, DateTime.UtcNow,
            origin: SavegameVersionOrigin.Created);

        dbContext.Savegames.Add(savegame);
        dbContext.SavegameVersions.Add(version);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var row = await dbContext.SavegameVersions.GetRowAsync(
            repoId, savegame.Id, version.Number, CancellationToken.None);

        Assert.Null(row!.ProfileId);
        Assert.Null(row.ProfileRevision);
    }


    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepoWithAProfile()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);
        var revision = profile.CreateRevision([], [], _author, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created);

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }

    private async Task<SavegameId> GivenACurrentSavegame(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    /// <summary>
    /// What PublishSavegameV1Endpoint does, in the order it does it: the incumbent leaves the slot in
    /// its own write, and only then does the newcomer take it.
    /// </summary>
    private async Task<SavegameId> GivenAPublishThatSupersedes(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var incumbent = await dbContext.Savegames.GetCurrentAsync(repoId, profileId, CancellationToken.None);

        incumbent?.Supersede(_supersededAt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    private async Task GivenAVersion(RepoId repoId, SavegameId savegameId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

        dbContext.SavegameVersions.Add(savegame.CreateVersion(
            new RevisionNumber(1),
            new string('3', ModImageHash.Length),
            1024,
            _author,
            DateTime.UtcNow,
            origin: SavegameVersionOrigin.Created));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
