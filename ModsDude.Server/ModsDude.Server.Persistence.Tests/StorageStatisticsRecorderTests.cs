using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Statistics;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// Every test samples a date of its own: the database is shared, and a run replaces everything
/// recorded for its date.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class StorageStatisticsRecorderTests(DatabaseFixture fixture)
{
    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static readonly DateTimeOffset _timestamp = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task Stored_and_registered_bytes_are_recorded_per_repo()
    {
        var date = new DateOnly(2001, 1, 1);
        var (repoId, savegameId) = await GivenARepo(modVersionSizes: [100, 200], snapshots: [(_hash, 1000), (_hash, 1000), (_otherHash, 50)]);

        var storage = new FakeBlobStorage();
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/2", 999));
        storage.Savegames.Add(Blob($"{repoId.Value}/{savegameId.Value}/{_hash}", 1000));

        await Record(storage, date);

        var samples = await SamplesOn(date);
        var mods = Assert.Single(samples, x => x.Container is StorageContainer.Mods && x.RepoId == repoId);
        var savegames = Assert.Single(samples, x => x.Container is StorageContainer.Savegames && x.RepoId == repoId);

        Assert.Equal((1009, 2, 300L), (mods.StoredBytes, mods.BlobCount, mods.RegisteredBytes));

        // Two snapshots share the first blob, so it is registered once.
        Assert.Equal((1000, 1, 1050L), (savegames.StoredBytes, savegames.BlobCount, savegames.RegisteredBytes));
    }

    [Fact]
    public async Task A_rerun_on_the_same_day_replaces_that_days_samples()
    {
        var date = new DateOnly(2001, 1, 2);
        var (repoId, _) = await GivenARepo(modVersionSizes: [100], snapshots: []);

        var first = new FakeBlobStorage();
        first.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));
        await Record(first, date);

        var second = new FakeBlobStorage();
        second.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));
        second.Mods.Add(Blob($"{repoId.Value}/a_mod/2", 20));
        await Record(second, date);

        var samples = await SamplesOn(date);

        var mods = Assert.Single(samples, x => x.Container is StorageContainer.Mods && x.RepoId == repoId);
        Assert.Equal(30, mods.StoredBytes);
        Assert.Single(samples, x => x.Container is StorageContainer.Images);
    }

    [Fact]
    public async Task Another_days_samples_are_left_alone()
    {
        var yesterday = new DateOnly(2001, 1, 3);
        var today = yesterday.AddDays(1);
        var (repoId, _) = await GivenARepo(modVersionSizes: [], snapshots: []);

        var storage = new FakeBlobStorage();
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));
        await Record(storage, yesterday);

        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/2", 20));
        await Record(storage, today);

        Assert.Equal(10, Assert.Single(await SamplesOn(yesterday), x => x.RepoId == repoId).StoredBytes);
        Assert.Equal(30, Assert.Single(await SamplesOn(today), x => x.RepoId == repoId).StoredBytes);
    }

    [Fact]
    public async Task A_run_cancelled_while_listing_keeps_the_samples_already_recorded()
    {
        var date = new DateOnly(2001, 1, 5);
        var (repoId, _) = await GivenARepo(modVersionSizes: [], snapshots: []);

        var storage = new FakeBlobStorage();
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));
        await Record(storage, date);

        using var cancellation = new CancellationTokenSource();
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/2", 20));
        storage.BeforeEachBlob = _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Record(storage, date, cancellation.Token));

        Assert.Equal(10, Assert.Single(await SamplesOn(date), x => x.RepoId == repoId).StoredBytes);
    }

    [Fact]
    public async Task A_run_failing_while_listing_keeps_the_samples_already_recorded()
    {
        var date = new DateOnly(2001, 1, 6);
        var (repoId, _) = await GivenARepo(modVersionSizes: [], snapshots: []);

        var storage = new FakeBlobStorage();
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));
        await Record(storage, date);

        storage.Images.Add(Blob($"{_hash[..2]}/{_hash}", 5));
        storage.BeforeEachBlob = blob =>
        {
            if (blob.Name.StartsWith(_hash[..2], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Storage went away.");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Record(storage, date));

        var samples = await SamplesOn(date);
        Assert.Equal(10, Assert.Single(samples, x => x.RepoId == repoId).StoredBytes);
        Assert.Equal(0, Assert.Single(samples, x => x.Container is StorageContainer.Images).StoredBytes);
    }

    [Fact]
    public async Task Concurrent_runs_for_one_day_never_leave_two_sets_of_samples()
    {
        var date = new DateOnly(2001, 1, 7);
        var (repoId, _) = await GivenARepo(modVersionSizes: [], snapshots: []);

        var storage = new FakeBlobStorage();
        storage.Mods.Add(Blob($"{repoId.Value}/a_mod/1", 10));

        var runs = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                await Record(storage, date);
            }
            catch (DbUpdateException)
            {
                // Losing to a concurrent run on the unique index is the expected way for one to fail.
            }
        }));

        await Task.WhenAll(runs);

        var samples = await SamplesOn(date);
        Assert.Single(samples, x => x.RepoId == repoId);
        Assert.Single(samples, x => x.Container is StorageContainer.Images);
    }

    [Fact]
    public async Task The_database_refuses_a_second_sample_for_the_same_day_container_and_repo()
    {
        var date = new DateOnly(2001, 1, 8);

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.StorageUsageSamples.Add(new StorageUsageSample(date, StorageContainer.Images, null, 1, 1, null));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            // No repo on either: nulls count as equal here, or images could be sampled twice a day.
            dbContext.StorageUsageSamples.Add(new StorageUsageSample(date, StorageContainer.Images, null, 2, 2, null));

            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Registered_savegame_bytes_count_each_blob_address_once()
    {
        var (repoId, _) = await GivenARepo(modVersionSizes: [], snapshots: [(_hash, 700), (_otherHash, 30), (_hash, 700)]);

        using var dbContext = fixture.CreateDbContext();

        var registered = await dbContext.SavegameSnapshots.GetRegisteredBytesPerRepoAsync(CancellationToken.None);

        Assert.Equal(730, registered[repoId]);
    }


    private async Task Record(FakeBlobStorage storage, DateOnly date, CancellationToken cancellationToken = default)
    {
        using var dbContext = fixture.CreateDbContext();

        var recorder = new StorageStatisticsRecorder(dbContext, storage, storage, storage, NullLogger<StorageStatisticsRecorder>.Instance);

        await recorder.RecordAsync(date, cancellationToken);
    }

    private async Task<List<StorageUsageSample>> SamplesOn(DateOnly date)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.StorageUsageSamples.GetOnDateAsync(date, CancellationToken.None);
    }

    private static StoredBlob Blob(string name, long length)
    {
        return new StoredBlob(name, _timestamp, length, null);
    }

    private async Task<(RepoId RepoId, SavegameId SavegameId)> GivenARepo(long[] modVersionSizes, (string Hash, long Size)[] snapshots)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);

        foreach (var (size, index) in modVersionSizes.Select((x, i) => (x, i)))
        {
            dbContext.ModVersions.Add(new ModVersion
            {
                RepoId = repo.Id,
                ModId = new ModId("a_mod"),
                Id = new ModVersionId($"{index + 1}"),
                SequenceNumber = index,
                DisplayName = "A mod",
                Description = "",
                FileName = "a_mod.zip",
                ContentHash = _hash,
                SizeBytes = size,
                Locked = false,
                Attributes = [],
                Created = _timestamp,
                Updated = _timestamp
            });
        }

        var savegame = new Savegame(repo.Id, new SavegameName($"save-{Guid.NewGuid()}"), null, DateTime.UtcNow);
        dbContext.Savegames.Add(savegame);

        foreach (var (hash, size) in snapshots)
        {
            dbContext.SavegameSnapshots.Add(savegame.CreateSnapshot(null, hash, size, userId, DateTime.UtcNow));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, savegame.Id);
    }
}
