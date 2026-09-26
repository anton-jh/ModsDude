using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Retention;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The retention jobs against a real database: that the facts the policy is fed - what a snapshot
/// was played on, what a revision pins, which version is newest - are read the way the policy means
/// them, and that schedules and deletions land on exactly the rows decided.
/// </summary>
/// <remarks>
/// The jobs sweep every repo, so each test builds its own and asserts only on it.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class RetentionSweeperTests(DatabaseFixture fixture)
{
    private static readonly UserId _author = new("author");
    private static readonly DateOnly _today = new(2026, 9, 23);
    private static readonly DateTimeOffset _now = new(2026, 9, 23, 4, 0, 0, TimeSpan.Zero);
    private static readonly ModId _modId = new("FS25_RetainedMod");


    [Fact]
    public async Task Snapshots_older_than_the_newest_three_are_scheduled_thirty_days_out()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 5);

        await ScheduleAsync(_today);

        Assert.Equal(
            [
                (1, Scheduled(30, DeletionReason.OutsideWindow)),
                (2, Scheduled(30, DeletionReason.OutsideWindow)),
                (3, null),
                (4, null),
                (5, null)
            ],
            await SnapshotSchedulesAsync(repoId, savegameId));
    }

    [Fact]
    public async Task A_second_run_keeps_the_dates_the_first_one_set()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 4);

        await ScheduleAsync(_today);
        await ScheduleAsync(_today.AddDays(5));

        Assert.Equal(Scheduled(30, DeletionReason.OutsideWindow), (await SnapshotSchedulesAsync(repoId, savegameId))[0].Schedule);
    }

    [Fact]
    public async Task Due_snapshots_are_deleted_and_the_rest_are_not()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 5);

        await ScheduleAsync(_today);
        await DeleteAsync(_today.AddDays(29));

        Assert.Equal(5, (await SnapshotSchedulesAsync(repoId, savegameId)).Count);

        await DeleteAsync(_today.AddDays(30));

        Assert.Equal([3, 4, 5], (await SnapshotSchedulesAsync(repoId, savegameId)).Select(x => x.Number));
    }

    /// <summary>
    /// The second step: once nothing older remains, the history winds down to its newest row.
    /// </summary>
    [Fact]
    public async Task A_savegame_down_to_three_snapshots_winds_down_to_its_head()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 3);

        await ScheduleAsync(_today);

        Assert.Equal(
            [
                (1, Scheduled(30, DeletionReason.WindingDown)),
                (2, Scheduled(30, DeletionReason.WindingDown)),
                (3, null)
            ],
            await SnapshotSchedulesAsync(repoId, savegameId));

        await DeleteAsync(_today.AddDays(30));

        Assert.Equal([3], (await SnapshotSchedulesAsync(repoId, savegameId)).Select(x => x.Number));
    }

    /// <summary>
    /// A check-in ends the winding down: the old second snapshot is back inside the window, and the
    /// old third one is now outside it - a different reason, so it waits its own thirty days.
    /// </summary>
    [Fact]
    public async Task A_new_snapshot_unschedules_what_it_brought_back_into_the_window_at_once()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 3);

        await ScheduleAsync(_today);
        await GivenSnapshots(repoId, savegameId, 1);
        await ReleaseSavegameAsync(repoId, savegameId);

        Assert.Equal(
            [(1, null), (2, null), (3, null), (4, null)],
            await SnapshotSchedulesAsync(repoId, savegameId));

        await ScheduleAsync(_today.AddDays(1));

        Assert.Equal(Scheduled(31, DeletionReason.OutsideWindow), (await SnapshotSchedulesAsync(repoId, savegameId))[0].Schedule);
    }

    /// <summary>
    /// The deletion job asks the policy again. A schedule the upkeep never got to clear - here, one
    /// written by hand - must not delete a row that is no longer eligible for its reason.
    /// </summary>
    [Fact]
    public async Task A_due_snapshot_that_is_no_longer_eligible_for_its_reason_survives()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 4);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.SavegameSnapshots
                .Where(x => x.RepoId == repoId && x.SavegameId == savegameId && x.Number == new SavegameSnapshotNumber(2))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.DeletionScheduledFor, _today)
                    .SetProperty(y => y.DeletionReason, DeletionReason.WindingDown));
        }

        await DeleteAsync(_today);

        Assert.Equal([1, 2, 3, 4], (await SnapshotSchedulesAsync(repoId, savegameId)).Select(x => x.Number));
    }


    /// <summary>
    /// A stale "winding down" date on the third of four snapshots - left behind by a check-in whose
    /// clearing never ran. The first run deletes the oldest, which shrinks the history back to three
    /// and makes that stale reason true again; a second run the same day must not act on a date made
    /// for a decision that had lapsed. The first run clears it, so the second finds nothing.
    /// </summary>
    [Fact]
    public async Task A_second_deletion_run_the_same_day_deletes_nothing_the_first_kept()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegameWithSnapshots(repoId, profileId, 4);

        await ScheduleAsync(_today);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.SavegameSnapshots
                .Where(x => x.RepoId == repoId && x.SavegameId == savegameId && x.Number == new SavegameSnapshotNumber(2))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.DeletionScheduledFor, _today)
                    .SetProperty(y => y.DeletionReason, DeletionReason.WindingDown));
        }

        await DeleteAsync(_today.AddDays(30));

        Assert.Equal(
            [(2, null), (3, null), (4, null)],
            await SnapshotSchedulesAsync(repoId, savegameId));

        await DeleteAsync(_today.AddDays(30));

        Assert.Equal([2, 3, 4], (await SnapshotSchedulesAsync(repoId, savegameId)).Select(x => x.Number));
    }


    [Fact]
    public async Task A_revision_a_snapshot_was_played_on_is_never_scheduled()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 4);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(1));

        await ScheduleAsync(_today);

        Assert.Equal(
            [(1, null), (2, Scheduled(14, DeletionReason.OutsideWindow)), (3, null), (4, null), (5, null)],
            await RevisionSchedulesAsync(repoId, profileId));
    }

    [Fact]
    public async Task A_profile_no_savegame_was_played_on_winds_down_to_its_head()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 2);

        await ScheduleAsync(_today);

        Assert.Equal(
            [(1, Scheduled(14, DeletionReason.WindingDown)), (2, Scheduled(14, DeletionReason.WindingDown)), (3, null)],
            await RevisionSchedulesAsync(repoId, profileId));

        await DeleteAsync(_today.AddDays(14));

        Assert.Equal([3], (await RevisionSchedulesAsync(repoId, profileId)).Select(x => x.Number));
    }

    [Fact]
    public async Task A_profile_with_any_revision_played_on_keeps_its_newest_three()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 2);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(3));

        await ScheduleAsync(_today);

        Assert.All(await RevisionSchedulesAsync(repoId, profileId), x => Assert.Null(x.Schedule));
    }

    [Fact]
    public async Task Playing_a_winding_down_profile_unschedules_its_revisions_at_once()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 2);

        await ScheduleAsync(_today);

        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(3));
        await ReleaseProfileAsync(repoId, profileId);

        Assert.All(await RevisionSchedulesAsync(repoId, profileId), x => Assert.Null(x.Schedule));
    }

    /// <summary>
    /// The play under an open claim is named by no snapshot until it is checked in, and the check-in
    /// is refused if the revision it names has gone. So the claim holds its revision and every later
    /// one - and nothing older.
    /// </summary>
    [Fact]
    public async Task An_open_checkout_holds_its_revision_and_every_later_one()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 5);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(1));
        await GivenACheckout(repoId, savegameId, holdsFrom: new RevisionNumber(3));

        await ScheduleAsync(_today);

        Assert.Equal(
            [(1, null), (2, Scheduled(14, DeletionReason.OutsideWindow)), (3, null), (4, null), (5, null), (6, null)],
            await RevisionSchedulesAsync(repoId, profileId));
    }

    [Fact]
    public async Task An_ended_checkout_holds_nothing()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 5);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(1));
        await GivenACheckout(repoId, savegameId, holdsFrom: new RevisionNumber(2), ended: true);

        await ScheduleAsync(_today);

        Assert.Equal(
            [(1, null), (2, Scheduled(14, DeletionReason.OutsideWindow)), (3, Scheduled(14, DeletionReason.OutsideWindow)), (4, null), (5, null), (6, null)],
            await RevisionSchedulesAsync(repoId, profileId));
    }

    /// <summary>
    /// The case that lost play: revisions scheduled while nobody had the save, then a check-out before
    /// they came due. Nothing cleared the dates, and the deletion job still has to keep them.
    /// </summary>
    [Fact]
    public async Task A_checkout_taken_after_scheduling_keeps_its_revisions_from_being_deleted()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 5);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(1));

        await ScheduleAsync(_today);
        await GivenACheckout(repoId, savegameId, holdsFrom: new RevisionNumber(2));
        await DeleteAsync(_today.AddDays(14));

        Assert.Equal(
            [(1, null), (2, null), (3, null), (4, null), (5, null), (6, null)],
            await RevisionSchedulesAsync(repoId, profileId));
    }

    [Fact]
    public async Task Taking_a_checkout_unschedules_the_revisions_it_holds_at_once()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 5);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(1));

        await ScheduleAsync(_today);
        await GivenACheckout(repoId, savegameId, holdsFrom: new RevisionNumber(3));
        await ReleaseProfileAsync(repoId, profileId);

        Assert.Equal(
            [(1, null), (2, Scheduled(14, DeletionReason.OutsideWindow)), (3, null), (4, null), (5, null), (6, null)],
            await RevisionSchedulesAsync(repoId, profileId));
    }

    /// <summary>A claim on another profile's savegame says nothing about this one's revisions.</summary>
    [Fact]
    public async Task A_checkout_holds_only_its_own_profiles_revisions()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        await GivenRevisions(repoId, profileId, 5);
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, 1, playedOn: new RevisionNumber(1));

        var otherProfileId = await GivenAProfile(repoId);
        var otherSavegameId = await GivenASavegame(repoId, otherProfileId);
        await GivenSnapshots(repoId, otherSavegameId, 1, playedOn: new RevisionNumber(1));
        await GivenACheckout(repoId, otherSavegameId, holdsFrom: new RevisionNumber(1));

        await ScheduleAsync(_today);

        Assert.Equal(
            [(1, null), (2, Scheduled(14, DeletionReason.OutsideWindow)), (3, Scheduled(14, DeletionReason.OutsideWindow)), (4, null), (5, null), (6, null)],
            await RevisionSchedulesAsync(repoId, profileId));
    }


    [Fact]
    public async Task Mod_versions_older_than_the_newest_two_are_scheduled_and_the_row_moves_in_the_delta_feed()
    {
        var repoId = await GivenAModWithVersions("1", "2", "3");

        await ScheduleAsync(_today);

        var versions = await VersionsAsync(repoId);

        Assert.Equal(
            [("1", Scheduled(14, DeletionReason.OutsideWindow)), ("2", null), ("3", null)],
            versions.Select(x => (x.Id.Value, Schedule(x.DeletionScheduledFor, x.DeletionReason))));
        Assert.Equal(_now, versions[0].Updated);
    }

    [Fact]
    public async Task A_pinned_mod_version_is_never_scheduled_and_its_mod_never_winds_down()
    {
        var repoId = await GivenAModWithVersions("1", "2");
        await GivenAProfilePinning(repoId, "1");

        await ScheduleAsync(_today);

        Assert.All(await VersionsAsync(repoId), x => Assert.Null(x.DeletionScheduledFor));
    }

    [Fact]
    public async Task Deleting_a_mod_version_closes_the_gap_in_the_order()
    {
        var repoId = await GivenAModWithVersions("1", "2", "3", "4");

        await ScheduleAsync(_today);
        await DeleteAsync(_today.AddDays(14));

        var versions = await VersionsAsync(repoId);

        Assert.Equal([("3", 0), ("4", 1)], versions.Select(x => (x.Id.Value, x.SequenceNumber)));
    }

    [Fact]
    public async Task Pinning_a_scheduled_version_unschedules_it_and_its_siblings_at_once()
    {
        var repoId = await GivenAModWithVersions("1", "2");

        await ScheduleAsync(_today);
        Assert.NotNull((await VersionsAsync(repoId))[0].DeletionScheduledFor);

        var profileId = await GivenAProfilePinning(repoId, "2");

        using (var dbContext = fixture.CreateDbContext())
        {
            await CreateUpkeep(dbContext).ReleaseModsPinnedByAsync(repoId, profileId, new RevisionNumber(1), CancellationToken.None);
        }

        Assert.All(await VersionsAsync(repoId), x => Assert.Null(x.DeletionScheduledFor));
    }


    private static DeletionSchedule Scheduled(int days, DeletionReason reason) => new(_today.AddDays(days), reason);

    private static DeletionSchedule? Schedule(DateOnly? date, DeletionReason? reason)
        => date is { } value && reason is { } why ? new DeletionSchedule(value, why) : null;

    private async Task ScheduleAsync(DateOnly today)
    {
        using var dbContext = fixture.CreateDbContext();

        await new RetentionSweeper(dbContext, NullLogger<RetentionSweeper>.Instance).ScheduleAsync(today, _now, CancellationToken.None);
    }

    private async Task DeleteAsync(DateOnly today)
    {
        using var dbContext = fixture.CreateDbContext();

        await new RetentionSweeper(dbContext, NullLogger<RetentionSweeper>.Instance).DeleteDueAsync(today, _now, CancellationToken.None);
    }

    private async Task ReleaseSavegameAsync(RepoId repoId, SavegameId savegameId)
    {
        using var dbContext = fixture.CreateDbContext();

        await CreateUpkeep(dbContext).ReleaseSavegameAsync(repoId, savegameId, CancellationToken.None);
    }

    private async Task ReleaseProfileAsync(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        await CreateUpkeep(dbContext).ReleaseProfileAsync(repoId, profileId, CancellationToken.None);
    }

    private static RetentionUpkeep CreateUpkeep(ApplicationDbContext dbContext)
        => new(dbContext, new FixedTime(), NullLogger<RetentionUpkeep>.Instance);

    private async Task<List<(int Number, DeletionSchedule? Schedule)>> SnapshotSchedulesAsync(RepoId repoId, SavegameId savegameId)
    {
        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.SavegameSnapshots
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Number, x.DeletionScheduledFor, x.DeletionReason })
            .ToListAsync();

        return [.. rows.Select(x => (x.Number.Value, Schedule(x.DeletionScheduledFor, x.DeletionReason)))];
    }

    private async Task<List<(int Number, DeletionSchedule? Schedule)>> RevisionSchedulesAsync(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.ProfileRevisions
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Number, x.DeletionScheduledFor, x.DeletionReason })
            .ToListAsync();

        return [.. rows.Select(x => (x.Number.Value, Schedule(x.DeletionScheduledFor, x.DeletionReason)))];
    }

    private async Task<List<ModVersion>> VersionsAsync(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.ModVersions
            .Where(x => x.RepoId == repoId)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync();
    }


    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepoWithAProfile()
    {
        RepoId repoId;

        using (var dbContext = fixture.CreateDbContext())
        {
            repoId = GivenARepo(dbContext).Id;
            await dbContext.SaveChangesAsync();
        }

        return (repoId, await GivenAProfile(repoId));
    }

    private async Task<ProfileId> GivenAProfile(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);
        var revision = profile.CreateRevision([], [], _author, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created);

        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync();

        return profile.Id;
    }

    private static Repo GivenARepo(ApplicationDbContext dbContext)
    {
        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);

        return repo;
    }

    /// <summary>Further empty revisions on top of the one the profile was created with.</summary>
    private async Task GivenRevisions(RepoId repoId, ProfileId profileId, int count)
    {
        for (var index = 0; index < count; index++)
        {
            using var dbContext = fixture.CreateDbContext();

            var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;
            dbContext.ProfileRevisions.Add(profile.CreateRevision([], [], _author, DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);
        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync();

        return savegame.Id;
    }

    private async Task<SavegameId> GivenASavegameWithSnapshots(RepoId repoId, ProfileId profileId, int count)
    {
        var savegameId = await GivenASavegame(repoId, profileId);
        await GivenSnapshots(repoId, savegameId, count);

        return savegameId;
    }

    private async Task GivenSnapshots(RepoId repoId, SavegameId savegameId, int count, RevisionNumber? playedOn = null)
    {
        for (var index = 0; index < count; index++)
        {
            using var dbContext = fixture.CreateDbContext();

            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            dbContext.SavegameSnapshots.Add(savegame.CreateSnapshot(
                playedOn ?? new RevisionNumber(1),
                new string((char)('a' + index), ModImageHash.Length),
                sizeBytes: 1024,
                _author,
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task GivenACheckout(RepoId repoId, SavegameId savegameId, RevisionNumber holdsFrom, bool ended = false)
    {
        using var dbContext = fixture.CreateDbContext();

        var checkout = new SavegameCheckout(repoId, savegameId, _author, DateTime.UtcNow, holdsFrom);

        if (ended)
        {
            checkout.End(DateTime.UtcNow, SavegameCheckoutEndReason.CheckedIn);
        }

        dbContext.SavegameCheckouts.Add(checkout);

        await dbContext.SaveChangesAsync();
    }

    private async Task<RepoId> GivenAModWithVersions(params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var repo = GivenARepo(dbContext);

        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => new ModVersion
        {
            RepoId = repo.Id,
            ModId = _modId,
            Id = new ModVersionId(versionId),
            SequenceNumber = index,
            DisplayName = versionId,
            Description = "",
            FileName = $"{_modId.Value}.zip",
            ContentHash = versionId,
            SizeBytes = 1024,
            Locked = false,
            Attributes = [],
            Created = _now.AddDays(-100),
            Updated = _now.AddDays(-100)
        }));

        await dbContext.SaveChangesAsync();

        return repo.Id;
    }

    private async Task<ProfileId> GivenAProfilePinning(RepoId repoId, string versionId)
    {
        using var dbContext = fixture.CreateDbContext();

        var version = await dbContext.ModVersions.GetAsync(repoId, _modId, new ModVersionId(versionId), CancellationToken.None);
        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(profile.CreateRevision(
            [new ModDependency { ModVersion = version!, Locked = false }],
            [],
            _author,
            DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        return profile.Id;
    }


    private class FixedTime : ITimeService
    {
        public DateTime Now() => _now.UtcDateTime;
    }
}
