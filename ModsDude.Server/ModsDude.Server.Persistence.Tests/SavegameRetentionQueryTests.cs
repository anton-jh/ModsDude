using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What deleting snapshots leaves behind.
/// </summary>
/// <remarks>
/// Which snapshots go is <see cref="Domain.Retention.RetentionPolicy"/>'s decision, tested on its own
/// and through <see cref="RetentionSweeperTests"/>; this is the delete that carries it out. It is the
/// one operation here that destroys somebody's backups, so every property it relies on is worth
/// pinning: that the delete removes exactly what it names, that the gaps it leaves stay gaps, and that
/// a blob two snapshots share survives one of them going.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SavegameRetentionQueryTests(DatabaseFixture fixture)
{
    private static readonly UserId _author = new("author");


    [Fact]
    public async Task Pruning_removes_the_snapshots_it_names_and_leaves_the_rest()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), null), (HashOf('3'), null), (HashOf('4'), null));

        using var dbContext = fixture.CreateDbContext();

        var deleted = await dbContext.SavegameSnapshots.DeleteSnapshotsAsync(
            repoId, savegameId,
            [new SavegameSnapshotNumber(1), new SavegameSnapshotNumber(3)],
            CancellationToken.None);

        using var verification = fixture.CreateDbContext();

        var remaining = await RemainingAsync(verification, repoId, savegameId);

        Assert.Equal(2, deleted);
        Assert.Equal([2, 4], remaining.Order());
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

        await GivenSnapshots(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), null));

        var dbContext = fixture.CreateDbContext();
        var snapshots = dbContext.SavegameSnapshots;

        dbContext.Dispose();

        Assert.Equal(0, await snapshots.DeleteSnapshotsAsync(repoId, savegameId, [], CancellationToken.None));

        using var verification = fixture.CreateDbContext();

        var remaining = await RemainingAsync(verification, repoId, savegameId);

        Assert.Equal([1, 2], remaining.Order());
    }

    /// <summary>
    /// Snapshot numbers are said out loud - "put us back on 3" - so a number has to keep meaning the
    /// same save for as long as anybody might say it. The head is the authority on what comes next,
    /// and it does not move backwards when the rows beneath it go, so pruning leaves the gap and the
    /// next check-in carries on past it.
    /// </summary>
    [Fact]
    public async Task A_pruned_number_is_never_handed_out_again()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, (HashOf('1'), null), (HashOf('2'), null), (HashOf('3'), null));

        using (var pruning = fixture.CreateDbContext())
        {
            await pruning.SavegameSnapshots.DeleteSnapshotsAsync(
                repoId, savegameId,
                [new SavegameSnapshotNumber(1), new SavegameSnapshotNumber(2)],
                CancellationToken.None);
        }

        await GivenSnapshots(repoId, profileId, savegameId, (HashOf('4'), null));

        using var verification = fixture.CreateDbContext();

        var savegame = await verification.Savegames.GetAsync(repoId, savegameId, CancellationToken.None);
        var remaining = await RemainingAsync(verification, repoId, savegameId);

        Assert.Equal(new SavegameSnapshotNumber(4), savegame!.HeadSnapshot);
        Assert.Equal([3, 4], remaining.Order());
    }

    /// <summary>
    /// The property that lets pruning stop at the rows and leave the bytes to the sweep. Snapshots are
    /// addressed by content, so a restore - and a night that changed nothing - leaves two snapshots
    /// naming one blob. If deleting one row could take the address out of the registered set, the
    /// next sweep would delete a blob the surviving snapshot still points at, and the save behind it
    /// is gone.
    /// </summary>
    [Fact]
    public async Task A_blob_two_snapshots_share_stays_registered_when_one_of_them_is_pruned()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var shared = HashOf('a');

        await GivenSnapshots(repoId, profileId, savegameId, (shared, null), (HashOf('b'), null), (shared, null));

        using (var pruning = fixture.CreateDbContext())
        {
            await pruning.SavegameSnapshots.DeleteSnapshotsAsync(
                repoId, savegameId,
                [new SavegameSnapshotNumber(1)],
                CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        var registered = await verification.SavegameSnapshots.GetRegisteredBlobAddressesAsync(CancellationToken.None);

        Assert.Contains(new SavegameBlobAddress(repoId, savegameId, shared), registered);
    }

    /// <summary>
    /// The other side of it: an address no surviving snapshot names must fall out of the set, or the
    /// sweep never reclaims anything and pruning saves no storage at all.
    /// </summary>
    [Fact]
    public async Task An_address_the_last_snapshot_naming_it_was_pruned_from_stops_being_registered()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var dropped = HashOf('c');

        await GivenSnapshots(repoId, profileId, savegameId, (dropped, null), (HashOf('d'), null));

        using (var pruning = fixture.CreateDbContext())
        {
            await pruning.SavegameSnapshots.DeleteSnapshotsAsync(
                repoId, savegameId,
                [new SavegameSnapshotNumber(1)],
                CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        var registered = await verification.SavegameSnapshots.GetRegisteredBlobAddressesAsync(CancellationToken.None);

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

        await GivenSnapshots(repoId, profileId, first, (shared, null));
        await GivenSnapshots(repoId, profileId, second, (shared, null));

        using var dbContext = fixture.CreateDbContext();

        var registered = await dbContext.SavegameSnapshots.GetRegisteredBlobAddressesAsync(CancellationToken.None);

        Assert.Contains(new SavegameBlobAddress(repoId, first, shared), registered);
        Assert.Contains(new SavegameBlobAddress(repoId, second, shared), registered);
    }


    private static string HashOf(char character) => new(character, ModImageHash.Length);

    private static async Task<List<int>> RemainingAsync(DbContexts.ApplicationDbContext dbContext, RepoId repoId, SavegameId savegameId)
    {
        var numbers = await dbContext.SavegameSnapshots
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId)
            .Select(x => x.Number)
            .ToListAsync(CancellationToken.None);

        return [.. numbers.Select(x => x.Value)];
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

    /// <summary>
    /// The same write a publish makes. A profile has one current savegame, so a second savegame on one
    /// profile supersedes the first rather than sitting beside it - and the two writes are ordered,
    /// because the index refuses the instant where both are current.
    /// </summary>
    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var superseded = await dbContext.Savegames.GetCurrentAsync(repoId, profileId, CancellationToken.None);

        superseded?.Supersede(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    /// <summary>
    /// The same write a check-in makes, one snapshot per pair: the head moves and the snapshot is
    /// numbered by the savegame rather than by the caller.
    /// </summary>
    private async Task GivenSnapshots(
        RepoId repoId, ProfileId profileId, SavegameId savegameId,
        params (string ContentHash, string? Label)[] snapshots)
    {
        foreach (var (contentHash, label) in snapshots)
        {
            using var dbContext = fixture.CreateDbContext();

            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            var snapshot = savegame.CreateSnapshot(
                new RevisionNumber(1),
                contentHash,
                sizeBytes: 1024,
                _author,
                DateTime.UtcNow,
                label);

            dbContext.SavegameSnapshots.Add(snapshot);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }
}
