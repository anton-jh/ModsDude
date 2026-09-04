using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What pruning reads before it decides, and what it leaves behind afterwards.
/// </summary>
/// <remarks>
/// <see cref="SavegameRetention.PlanPrune"/> is pure and tested on its own; these are the two halves
/// around it that only a database can answer - the read that turns rows into the policy's input, and
/// the delete that carries the decision out. Pruning is the one operation here that destroys
/// somebody's backups, so every property it relies on is worth pinning: that a labelled version is
/// reported as labelled, that the delete removes exactly what it names, that the gaps it leaves stay
/// gaps, and that a blob two versions share survives one of them going.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SavegameRetentionQueryTests(DatabaseFixture fixture)
{
    private static readonly UserId _author = new("author");


    /// <summary>
    /// <c>IsLabelled</c> is the entire exemption rule, derived from a nullable column after the round
    /// trip. Reading it the wrong way round would prune exactly the versions somebody named to keep.
    /// </summary>
    [Fact]
    public async Task A_version_counts_as_labelled_exactly_when_somebody_named_it()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenVersions(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), "Before the harvest"), (HashOf('3'), null));

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.SavegameVersions.GetRetentionRowsAsync(repoId, savegameId, CancellationToken.None);

        Assert.Equal(
            [(1, false), (2, true), (3, false)],
            rows.OrderBy(x => x.Number).Select(x => (x.Number.Value, x.IsLabelled)));
    }

    /// <summary>
    /// The policy reasons about the whole set rather than a page of it, so the read is scoped by
    /// savegame and nothing else. A predicate that lost its savegame clause would plan a prune of one
    /// save from another save's history.
    /// </summary>
    [Fact]
    public async Task Retention_rows_cover_one_savegame_entirely_and_no_other_savegame_at_all()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var mine = await GivenASavegame(repoId, profileId);
        var theirs = await GivenASavegame(repoId, profileId);

        await GivenVersions(repoId, profileId, mine, (HashOf('1'), null), (HashOf('2'), null));
        await GivenVersions(repoId, profileId, theirs, (HashOf('3'), null), (HashOf('4'), null), (HashOf('5'), null));

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.SavegameVersions.GetRetentionRowsAsync(repoId, mine, CancellationToken.None);

        Assert.Equal([1, 2], rows.Select(x => x.Number.Value).Order());
    }

    [Fact]
    public async Task Pruning_removes_the_versions_it_names_and_leaves_the_rest()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenVersions(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), null), (HashOf('3'), null), (HashOf('4'), null));

        using var dbContext = fixture.CreateDbContext();

        var deleted = await dbContext.SavegameVersions.DeleteVersionsAsync(
            repoId, savegameId,
            [new SavegameVersionNumber(1), new SavegameVersionNumber(3)],
            CancellationToken.None);

        using var verification = fixture.CreateDbContext();

        var remaining = await verification.SavegameVersions.GetRetentionRowsAsync(repoId, savegameId, CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal([2, 4], remaining.Select(x => x.Number.Value).Order());
    }

    /// <summary>
    /// A savegame whose policy plans nothing to prune is the ordinary case - every check-in below the
    /// retention limit reaches this. The guard returns before the query is built, which is why this
    /// asserts against a context that has already been disposed: an <c>ExecuteDelete</c> would fault,
    /// so returning zero is proof that no statement was issued rather than that one was harmless.
    /// </summary>
    [Fact]
    public async Task Pruning_nothing_issues_no_delete_at_all()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenVersions(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), null));

        var dbContext = fixture.CreateDbContext();
        var versions = dbContext.SavegameVersions;

        dbContext.Dispose();

        Assert.Equal(0, await versions.DeleteVersionsAsync(repoId, savegameId, [], CancellationToken.None));

        using var verification = fixture.CreateDbContext();

        var remaining = await verification.SavegameVersions.GetRetentionRowsAsync(repoId, savegameId, CancellationToken.None);

        Assert.Equal([1, 2], remaining.Select(x => x.Number.Value).Order());
    }

    /// <summary>
    /// Version numbers are said out loud - "put us back on 3" - so a number has to keep meaning the
    /// same save for as long as anybody might say it. The head is the authority on what comes next,
    /// and it does not move backwards when the rows beneath it go, so pruning leaves the gap and the
    /// next check-in carries on past it.
    /// </summary>
    [Fact]
    public async Task A_pruned_number_is_never_handed_out_again()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenVersions(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), null), (HashOf('3'), null));

        using (var pruning = fixture.CreateDbContext())
        {
            await pruning.SavegameVersions.DeleteVersionsAsync(
                repoId, savegameId,
                [new SavegameVersionNumber(1), new SavegameVersionNumber(2)],
                CancellationToken.None);
        }

        await GivenVersions(repoId, profileId, savegameId, (HashOf('4'), null));

        using var verification = fixture.CreateDbContext();

        var savegame = await verification.Savegames.GetAsync(repoId, savegameId, CancellationToken.None);
        var remaining = await verification.SavegameVersions.GetRetentionRowsAsync(repoId, savegameId, CancellationToken.None);

        Assert.Equal(new SavegameVersionNumber(4), savegame!.HeadVersion);
        Assert.Equal([3, 4], remaining.Select(x => x.Number.Value).Order());
    }

    /// <summary>
    /// The property that lets pruning stop at the rows and leave the bytes to the sweep. Versions are
    /// addressed by content, so a restore - and a night that changed nothing - leaves two versions
    /// naming one blob. If deleting one row could take the address out of the registered set, the
    /// next sweep would delete a blob the surviving version still points at, and the save behind it
    /// is gone.
    /// </summary>
    [Fact]
    public async Task A_blob_two_versions_share_stays_registered_when_one_of_them_is_pruned()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var shared = HashOf('a');

        await GivenVersions(repoId, profileId, savegameId, (shared, null), (HashOf('b'), null), (shared, null));

        using (var pruning = fixture.CreateDbContext())
        {
            await pruning.SavegameVersions.DeleteVersionsAsync(
                repoId, savegameId,
                [new SavegameVersionNumber(1)],
                CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        var registered = await verification.SavegameVersions.GetRegisteredBlobAddressesAsync(CancellationToken.None);

        Assert.Contains(new SavegameBlobAddress(repoId, savegameId, shared), registered);
    }

    /// <summary>
    /// The other side of it: an address no surviving version names must fall out of the set, or the
    /// sweep never reclaims anything and pruning saves no storage at all.
    /// </summary>
    [Fact]
    public async Task An_address_the_last_version_naming_it_was_pruned_from_stops_being_registered()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var dropped = HashOf('c');

        await GivenVersions(repoId, profileId, savegameId, (dropped, null), (HashOf('d'), null));

        using (var pruning = fixture.CreateDbContext())
        {
            await pruning.SavegameVersions.DeleteVersionsAsync(
                repoId, savegameId,
                [new SavegameVersionNumber(1)],
                CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        var registered = await verification.SavegameVersions.GetRegisteredBlobAddressesAsync(CancellationToken.None);

        Assert.DoesNotContain(new SavegameBlobAddress(repoId, savegameId, dropped), registered);
        Assert.Contains(new SavegameBlobAddress(repoId, savegameId, HashOf('d')), registered);
    }

    /// <summary>
    /// The sweep reads the whole store rather than one repo, and the addresses come back
    /// deduplicated by the database. Two savegames that happen to hold identical bytes are still two
    /// addresses, because the savegame id is part of the path.
    /// </summary>
    [Fact]
    public async Task The_same_bytes_under_two_savegames_are_two_addresses()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var first = await GivenASavegame(repoId, profileId);
        var second = await GivenASavegame(repoId, profileId);

        var shared = HashOf('e');

        await GivenVersions(repoId, profileId, first, (shared, null));
        await GivenVersions(repoId, profileId, second, (shared, null));

        using var dbContext = fixture.CreateDbContext();

        var registered = await dbContext.SavegameVersions.GetRegisteredBlobAddressesAsync(CancellationToken.None);

        Assert.Contains(new SavegameBlobAddress(repoId, first, shared), registered);
        Assert.Contains(new SavegameBlobAddress(repoId, second, shared), registered);
    }


    private static string HashOf(char character) => new(character, ModImageHash.Length);

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

    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    /// <summary>
    /// The same write a check-in makes, one version per pair: the head moves and the version is
    /// numbered by the savegame rather than by the caller.
    /// </summary>
    private async Task GivenVersions(
        RepoId repoId, ProfileId profileId, SavegameId savegameId,
        params (string ContentHash, string? Label)[] versions)
    {
        foreach (var (contentHash, label) in versions)
        {
            using var dbContext = fixture.CreateDbContext();

            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            var version = savegame.CreateVersion(
                profileId,
                new RevisionNumber(1),
                contentHash,
                sizeBytes: 1024,
                _author,
                DateTime.UtcNow,
                label);

            dbContext.SavegameVersions.Add(version);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }
}
