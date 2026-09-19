using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The reads a savegame list and a savegame's detail pane are built out of - and the pairing
/// <see cref="SavegameExtensions.GetHeadSnapshotsAsync"/> has to do for itself.
/// </summary>
/// <remarks>
/// A repo's savegame list costs a fixed number of queries rather than one per row, which is only
/// possible because the head snapshots and the open claims are fetched in bulk and matched up
/// afterwards. That matching is ordinary C# sitting behind an over-reading SQL predicate, so nothing
/// about it fails loudly: get it wrong and the list renders, with one savegame quietly showing
/// another's snapshot.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SavegameListQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTime _takenAt = new(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc);
    private static readonly UserId _author = new("author");


    [Fact]
    public async Task The_list_holds_every_savegame_in_the_repo_and_nothing_from_another()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var (otherRepoId, otherProfileId) = await GivenARepoWithAProfile();

        await GivenASavegame(repoId, profileId, "Gamma");
        await GivenASavegame(repoId, profileId, "Alpha");
        await GivenASavegame(otherRepoId, otherProfileId, "Beta");

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None);

        // Ordered by name, because the order is settled once in the database rather than by whichever
        // caller happens to render the list.
        Assert.Equal(["Alpha", "Gamma"], rows.Select(x => x.Name.Value));
    }

    /// <summary>
    /// Everything the list renders about a savegame comes off its own row, through a projection into
    /// a constructor-bound record - so none of it may arrive as a default. The head especially: a
    /// zero there means "no snapshots yet", which is a state that only exists inside the transaction
    /// that publishes the save.
    /// </summary>
    [Fact]
    public async Task A_list_row_carries_what_the_savegame_says_about_itself()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId, "Season 4");

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'), HashOf('2'));

        using var dbContext = fixture.CreateDbContext();

        var row = Assert.Single(await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None));

        Assert.Equal(savegameId, row.Id);
        Assert.Equal("Season 4", row.Name.Value);
        Assert.Equal(profileId, row.ProfileId);
        Assert.Equal(new SavegameSnapshotNumber(2), row.HeadSnapshot);
    }

    /// <summary>
    /// The regression this method exists to fail on. A provider cannot translate a membership test
    /// over a tuple of two value objects, so the predicate asks for the cross product of the savegame
    /// ids and the head numbers and the exact pairing is done here, after the round trip. Two
    /// savegames whose numbering has crossed - one at head 2, the other with a snapshot 2 of its own
    /// that is not its head - is precisely the case an over-reading query answers wrongly, and it is
    /// the ordinary case as soon as two saves have been played a different number of times.
    /// </summary>
    [Fact]
    public async Task A_savegame_does_not_pick_up_another_savegames_snapshot_of_the_same_number()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var shorter = await GivenASavegame(repoId, profileId, "Shorter");
        var longer = await GivenASavegame(repoId, profileId, "Longer");

        await GivenSnapshots(repoId, profileId, shorter, HashOf('1'), HashOf('2'));
        await GivenSnapshots(repoId, profileId, longer, HashOf('3'), HashOf('4'), HashOf('5'));

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None);
        var heads = rows.ToDictionary(x => x.Id, x => x.HeadSnapshot);

        var snapshots = await dbContext.SavegameSnapshots.GetHeadSnapshotsAsync(repoId, heads, CancellationToken.None);

        // Number 2 exists under both savegames and is the head of only one of them, so the cross
        // product read four rows to answer with two.
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(new SavegameSnapshotNumber(2), snapshots.Single(x => x.SavegameId == shorter).Number);
        Assert.Equal(new SavegameSnapshotNumber(3), snapshots.Single(x => x.SavegameId == longer).Number);
        Assert.Equal(HashOf('2'), snapshots.Single(x => x.SavegameId == shorter).ContentHash);
    }

    /// <summary>
    /// A repo with no savegames still renders a list, and it must not do so by asking the database
    /// for every snapshot of every savegame in the system - which is what an empty <c>IN</c> list
    /// would degrade into.
    /// </summary>
    [Fact]
    public async Task Asking_for_no_heads_reads_nothing_at_all()
    {
        var (repoId, _) = await GivenARepoWithAProfile();

        using var dbContext = fixture.CreateDbContext();

        Assert.Empty(await dbContext.Savegames.GetRowsAsync(repoId, CancellationToken.None));
        Assert.Empty(await dbContext.SavegameSnapshots.GetHeadSnapshotsAsync(
            repoId,
            new Dictionary<SavegameId, SavegameSnapshotNumber>(),
            CancellationToken.None));
    }

    /// <summary>
    /// Ordered and windowed before it is projected, for the reason the profile history gives: a
    /// provider cannot see through a constructor-bound record, so ordering by a member of the
    /// projection has nowhere to go - and the refusal is a runtime error on the page rather than a
    /// build error.
    /// </summary>
    [Fact]
    public async Task The_snapshot_history_reads_newest_first()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'), HashOf('2'), HashOf('3'));

        using var dbContext = fixture.CreateDbContext();

        var history = await dbContext.SavegameSnapshots.GetHistoryAsync(repoId, savegameId, 0, 50, CancellationToken.None);

        Assert.Equal([3, 2, 1], history.Select(x => x.Number.Value));
    }

    [Fact]
    public async Task The_snapshot_history_is_windowed_so_it_can_be_paged()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'), HashOf('2'), HashOf('3'), HashOf('4'));

        using var dbContext = fixture.CreateDbContext();

        var first = await dbContext.SavegameSnapshots.GetHistoryAsync(repoId, savegameId, 0, 2, CancellationToken.None);
        var second = await dbContext.SavegameSnapshots.GetHistoryAsync(repoId, savegameId, 2, 2, CancellationToken.None);

        Assert.Equal([4, 3], first.Select(x => x.Number.Value));
        Assert.Equal([2, 1], second.Select(x => x.Number.Value));
    }

    /// <summary>
    /// Everything a history row renders comes off the snapshot's own row, including the three nullable
    /// fields that describe how it came to exist. <c>Origin</c> is stored through a string conversion
    /// and <c>BaseSnapshot</c> is a nullable value object, either of which could round trip to
    /// something plausible and wrong.
    /// </summary>
    [Fact]
    public async Task A_history_row_carries_what_the_snapshot_recorded_about_itself()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);
        var checkoutId = await GivenAnOpenCheckout(repoId, savegameId);

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'));

        using (var dbContext = fixture.CreateDbContext())
        {
            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            dbContext.SavegameSnapshots.Add(savegame.CreateSnapshot(
                new RevisionNumber(1),
                HashOf('2'),
                sizeBytes: 4096,
                _author,
                _takenAt,
                label: "Before the harvest",
                origin: SavegameSnapshotOrigin.Forced,
                baseSnapshot: new SavegameSnapshotNumber(1),
                checkoutId: checkoutId));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        var row = await verification.SavegameSnapshots.GetRowAsync(repoId, savegameId, new SavegameSnapshotNumber(2), CancellationToken.None);

        Assert.Equal("Before the harvest", row!.Label);
        Assert.Equal(SavegameSnapshotOrigin.Forced, row.Origin);
        Assert.Equal(new SavegameSnapshotNumber(1), row.BaseSnapshot);
        Assert.Equal(checkoutId, row.CheckoutId);
        Assert.Equal(new RevisionNumber(1), row.ProfileRevision);
        Assert.Equal(4096, row.SizeBytes);
        Assert.Equal(_author, row.CreatedBy);
    }

    /// <summary>
    /// What an adapter said about a snapshot, round-tripped in its own order. Owned collections and a
    /// projection that never materializes the snapshot are the two things that could quietly lose
    /// them - and the order is a judgment the adapter made, so it is not the database's to reshuffle.
    /// </summary>
    [Fact]
    public async Task A_snapshots_details_survive_in_the_order_the_adapter_wanted_them_read()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            dbContext.SavegameSnapshots.Add(savegame.CreateSnapshot(
                new RevisionNumber(1),
                HashOf('1'),
                sizeBytes: 4096,
                _author,
                _takenAt,
                details:
                [
                    // Deliberately handed over out of order, so what comes back is Position's doing
                    // rather than the insertion order's.
                    new SavegameDetail("playtime", "Played", "45 h", 2),
                    new SavegameDetail("map", "Map", "Zielonka", 0),
                    new SavegameDetail("last-played", "Last played", "2024-11-30", 1)
                ]));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        var row = await verification.SavegameSnapshots.GetRowAsync(
            repoId, savegameId, new SavegameSnapshotNumber(1), CancellationToken.None);

        Assert.Equal(["map", "last-played", "playtime"], row!.Details.Select(x => x.Key));
        Assert.Equal(["Map", "Last played", "Played"], row.Details.Select(x => x.Label));
        Assert.Equal(["Zielonka", "2024-11-30", "45 h"], row.Details.Select(x => x.Value));
    }

    /// <summary>An adapter that describes nothing is an ordinary adapter, not a broken one.</summary>
    [Fact]
    public async Task A_snapshot_with_no_details_reads_as_having_none()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'));

        using var verification = fixture.CreateDbContext();

        var row = await verification.SavegameSnapshots.GetRowAsync(
            repoId, savegameId, new SavegameSnapshotNumber(1), CancellationToken.None);

        Assert.Empty(row!.Details);
    }

    /// <summary>
    /// The first snapshot of a savegame was built on nothing and was not checked in against a claim,
    /// so both nullable columns have to survive as nulls rather than as a zeroth snapshot somebody
    /// could try to restore.
    /// </summary>
    [Fact]
    public async Task The_first_snapshot_of_a_savegame_names_no_base_and_no_claim()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'));

        using var dbContext = fixture.CreateDbContext();

        var row = await dbContext.SavegameSnapshots.GetRowAsync(repoId, savegameId, new SavegameSnapshotNumber(1), CancellationToken.None);

        Assert.Null(row!.BaseSnapshot);
        Assert.Null(row.CheckoutId);
        Assert.Null(row.Label);
    }

    [Fact]
    public async Task A_snapshot_the_savegame_does_not_have_reads_as_absent_rather_than_throwing()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'));

        using var dbContext = fixture.CreateDbContext();

        Assert.Null(await dbContext.SavegameSnapshots.GetRowAsync(repoId, savegameId, new SavegameSnapshotNumber(7), CancellationToken.None));
    }

    /// <summary>
    /// Ordered by when a claim was taken rather than by when it ended, because the open row has no
    /// end and would sort first or last depending on the provider's opinion of nulls. Taking is what
    /// a reader is looking for anyway.
    /// </summary>
    [Fact]
    public async Task The_checkout_history_reads_newest_first()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenAnEndedCheckout(repoId, savegameId, _takenAt);
        await GivenAnEndedCheckout(repoId, savegameId, _takenAt.AddDays(1));
        await GivenAnOpenCheckout(repoId, savegameId, _takenAt.AddDays(2));

        using var dbContext = fixture.CreateDbContext();

        var history = await dbContext.SavegameCheckouts.GetHistoryAsync(repoId, savegameId, 0, 50, CancellationToken.None);

        Assert.Equal(
            [_takenAt.AddDays(2), _takenAt.AddDays(1), _takenAt],
            history.Select(x => x.TakenAt));
    }

    [Fact]
    public async Task The_checkout_history_is_windowed_so_it_can_be_paged()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        foreach (var day in Enumerable.Range(0, 4))
        {
            await GivenAnEndedCheckout(repoId, savegameId, _takenAt.AddDays(day));
        }

        using var dbContext = fixture.CreateDbContext();

        var first = await dbContext.SavegameCheckouts.GetHistoryAsync(repoId, savegameId, 0, 2, CancellationToken.None);
        var second = await dbContext.SavegameCheckouts.GetHistoryAsync(repoId, savegameId, 2, 2, CancellationToken.None);

        Assert.Equal([_takenAt.AddDays(3), _takenAt.AddDays(2)], first.Select(x => x.TakenAt));
        Assert.Equal([_takenAt.AddDays(1), _takenAt], second.Select(x => x.TakenAt));
        Assert.Equal(4, await dbContext.SavegameCheckouts.CountCheckoutsAsync(repoId, savegameId, CancellationToken.None));
    }

    [Fact]
    public async Task One_savegames_timeline_holds_nothing_of_another_savegames()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var mine = await GivenASavegame(repoId, profileId);
        var theirs = await GivenASavegame(repoId, profileId);

        await GivenSnapshots(repoId, profileId, mine, HashOf('1'));
        await GivenSnapshots(repoId, profileId, theirs, HashOf('2'), HashOf('3'));
        await GivenAnEndedCheckout(repoId, theirs, _takenAt);

        using var dbContext = fixture.CreateDbContext();

        Assert.Single(await dbContext.SavegameSnapshots.GetHistoryAsync(repoId, mine, 0, 50, CancellationToken.None));
        Assert.Equal(1, await dbContext.SavegameSnapshots.CountSnapshotsAsync(repoId, mine, CancellationToken.None));
        Assert.Empty(await dbContext.SavegameCheckouts.GetHistoryAsync(repoId, mine, 0, 50, CancellationToken.None));
    }


    [Fact]
    public async Task A_savegames_totals_count_its_snapshots_and_sum_its_blobs()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var many = await GivenASavegame(repoId, profileId, "Many");
        var few = await GivenASavegame(repoId, profileId, "Few");

        await GivenSnapshots(repoId, profileId, many, HashOf('1'), HashOf('2'), HashOf('3'));
        await GivenSnapshots(repoId, profileId, few, HashOf('4'));

        using var dbContext = fixture.CreateDbContext();

        var totals = await dbContext.SavegameSnapshots.GetSnapshotTotalsAsync(repoId, CancellationToken.None);

        Assert.Equal(new SavegameSnapshotTotals(3, 3 * 1024), totals[many]);
        Assert.Equal(new SavegameSnapshotTotals(1, 1024), totals[few]);
    }

    /// <summary>
    /// A restore copies an old snapshot forward under the hash it already had, and the blob is addressed
    /// by that hash - so two snapshots, one blob. Storage holds it once, and so does the total.
    /// </summary>
    [Fact]
    public async Task Snapshots_sharing_a_content_hash_are_one_blob_in_the_total()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId, "Restored");

        await GivenSnapshots(repoId, profileId, savegameId, HashOf('1'), HashOf('2'), HashOf('1'));

        using var dbContext = fixture.CreateDbContext();

        var totals = await dbContext.SavegameSnapshots.GetSnapshotTotalsAsync(repoId, CancellationToken.None);

        Assert.Equal(new SavegameSnapshotTotals(3, 2 * 1024), totals[savegameId]);
    }

    [Fact]
    public async Task Totals_can_be_asked_for_one_savegame_and_never_include_another_repos()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var (otherRepoId, otherProfileId) = await GivenARepoWithAProfile();
        var wanted = await GivenASavegame(repoId, profileId, "Wanted");
        var other = await GivenASavegame(repoId, profileId, "Other");
        var foreign = await GivenASavegame(otherRepoId, otherProfileId, "Foreign");

        await GivenSnapshots(repoId, profileId, wanted, HashOf('1'), HashOf('2'));
        await GivenSnapshots(repoId, profileId, other, HashOf('3'));
        await GivenSnapshots(otherRepoId, otherProfileId, foreign, HashOf('4'));

        using var dbContext = fixture.CreateDbContext();

        var totals = await dbContext.SavegameSnapshots.GetSnapshotTotalsAsync(repoId, CancellationToken.None, wanted);

        Assert.Equal(new SavegameSnapshotTotals(2, 2 * 1024), Assert.Single(totals).Value);
        Assert.Equal(wanted, Assert.Single(totals).Key);
    }

    /// <summary>A savegame with nothing stored has no entry, which callers read as zero.</summary>
    [Fact]
    public async Task A_savegame_with_no_snapshots_is_absent_from_the_totals()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var empty = await GivenASavegame(repoId, profileId, "Empty");

        using var dbContext = fixture.CreateDbContext();

        var totals = await dbContext.SavegameSnapshots.GetSnapshotTotalsAsync(repoId, CancellationToken.None);

        Assert.False(totals.ContainsKey(empty));
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

    /// <summary>
    /// The same write a publish makes. A profile has one current savegame, so a second savegame on one
    /// profile supersedes the first rather than sitting beside it - and the two writes are ordered,
    /// because the index refuses the instant where both are current.
    /// </summary>
    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId, string? name = null)
    {
        using var dbContext = fixture.CreateDbContext();

        var superseded = await dbContext.Savegames.GetCurrentAsync(repoId, profileId, CancellationToken.None);

        superseded?.Supersede(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var savegame = new Savegame(repoId, new SavegameName(name ?? $"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    private async Task GivenSnapshots(RepoId repoId, ProfileId profileId, SavegameId savegameId, params string[] contentHashes)
    {
        foreach (var contentHash in contentHashes)
        {
            using var dbContext = fixture.CreateDbContext();

            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            var snapshot = savegame.CreateSnapshot(
                new RevisionNumber(1),
                contentHash,
                sizeBytes: 1024,
                _author,
                DateTime.UtcNow);

            dbContext.SavegameSnapshots.Add(snapshot);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<SavegameCheckoutId> GivenAnOpenCheckout(RepoId repoId, SavegameId savegameId, DateTime? takenAt = null)
    {
        using var dbContext = fixture.CreateDbContext();

        var checkout = new SavegameCheckout(repoId, savegameId, _author, takenAt ?? _takenAt);

        dbContext.SavegameCheckouts.Add(checkout);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return checkout.Id;
    }

    private async Task GivenAnEndedCheckout(RepoId repoId, SavegameId savegameId, DateTime takenAt)
    {
        using var dbContext = fixture.CreateDbContext();

        var checkout = new SavegameCheckout(repoId, savegameId, _author, takenAt);

        checkout.End(takenAt.AddHours(2), SavegameCheckoutEndReason.CheckedIn);

        dbContext.SavegameCheckouts.Add(checkout);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
