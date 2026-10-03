using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Statistics;

namespace ModsDude.Server.Domain.Tests.Statistics;

public class StorageTallyTests
{
    private static readonly RepoId _repoA = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly RepoId _repoB = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly Guid _savegameId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly _date = new(2026, 10, 3);

    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";


    [Fact]
    public void Mod_and_savegame_blobs_are_added_up_per_repo()
    {
        var tally = new StorageTally();
        tally.Add(StorageContainer.Mods, Blob($"{_repoA.Value}/a_mod/1.0.0", 100));
        tally.Add(StorageContainer.Mods, Blob($"{_repoA.Value}/a_mod/2.0.0", 50));
        tally.Add(StorageContainer.Mods, Blob($"{_repoB.Value}/a_mod/1.0.0", 7));
        tally.Add(StorageContainer.Savegames, Blob($"{_repoA.Value}/{_savegameId}/{_hash}", 1000));

        var samples = tally.ToSamples(_date, Registered(), Registered());

        Assert.Equal((150, 2), Stored(samples, StorageContainer.Mods, _repoA));
        Assert.Equal((7, 1), Stored(samples, StorageContainer.Mods, _repoB));
        Assert.Equal((1000, 1), Stored(samples, StorageContainer.Savegames, _repoA));
    }

    [Fact]
    public void Images_are_totalled_without_a_repo()
    {
        var tally = new StorageTally();
        tally.Add(StorageContainer.Images, Blob($"{_hash[..2]}/{_hash}", 10));
        tally.Add(StorageContainer.Images, Blob($"{_otherHash[..2]}/{_otherHash}", 20));

        var images = Assert.Single(tally.ToSamples(_date, Registered(), Registered()));

        Assert.Equal(StorageContainer.Images, images.Container);
        Assert.Null(images.RepoId);
        Assert.Equal(30, images.StoredBytes);
        Assert.Equal(2, images.BlobCount);
        Assert.Null(images.RegisteredBytes);
    }

    [Fact]
    public void An_empty_image_container_still_gets_a_sample()
    {
        var samples = new StorageTally().ToSamples(_date, Registered(), Registered());

        var images = Assert.Single(samples);
        Assert.Equal(StorageContainer.Images, images.Container);
        Assert.Equal(0, images.StoredBytes);
    }

    [Fact]
    public void Blobs_outside_the_layout_are_counted_without_a_repo()
    {
        var tally = new StorageTally();
        tally.Add(StorageContainer.Mods, Blob("stray.txt", 5));
        tally.Add(StorageContainer.Savegames, Blob($"{_repoA.Value}/not-a-guid/{_hash}", 6));

        var samples = tally.ToSamples(_date, Registered(), Registered());

        Assert.Equal((5, 1), Stored(samples, StorageContainer.Mods, null));
        Assert.Equal((6, 1), Stored(samples, StorageContainer.Savegames, null));
        Assert.All(samples.Where(x => x.Container is not StorageContainer.Images), x => Assert.Equal(0, x.RegisteredBytes));
    }

    [Fact]
    public void Registered_bytes_are_matched_to_their_repo()
    {
        var tally = new StorageTally();
        tally.Add(StorageContainer.Mods, Blob($"{_repoA.Value}/a_mod/1.0.0", 100));

        var samples = tally.ToSamples(_date, Registered((_repoA, 90)), Registered((_repoA, 400)));

        Assert.Equal(90, Single(samples, StorageContainer.Mods, _repoA).RegisteredBytes);
        Assert.Equal(400, Single(samples, StorageContainer.Savegames, _repoA).RegisteredBytes);
    }

    [Fact]
    public void A_repo_with_registered_bytes_but_no_blobs_gets_a_sample()
    {
        // Every blob of the repo missing is exactly what the comparison exists to show.
        var samples = new StorageTally().ToSamples(_date, Registered((_repoB, 64)), Registered());

        var sample = Single(samples, StorageContainer.Mods, _repoB);
        Assert.Equal(0, sample.StoredBytes);
        Assert.Equal(64, sample.RegisteredBytes);
    }

    [Fact]
    public void Samples_come_out_in_the_same_order_whatever_order_the_blobs_came_in()
    {
        var blobs = new (StorageContainer Container, StoredBlob Blob)[]
        {
            (StorageContainer.Savegames, Blob($"{_repoB.Value}/{_savegameId}/{_hash}", 1)),
            (StorageContainer.Mods, Blob($"{_repoB.Value}/a_mod/1.0.0", 2)),
            (StorageContainer.Mods, Blob("stray.txt", 3)),
            (StorageContainer.Mods, Blob($"{_repoA.Value}/a_mod/1.0.0", 4)),
            (StorageContainer.Images, Blob($"{_hash[..2]}/{_hash}", 5))
        };

        var forwards = Keys(Tally(blobs));
        var backwards = Keys(Tally(blobs.Reverse()));

        Assert.Equal(forwards, backwards);
        Assert.Equal(
            [
                (StorageContainer.Mods, null),
                (StorageContainer.Mods, _repoA),
                (StorageContainer.Mods, _repoB),
                (StorageContainer.Savegames, _repoB),
                (StorageContainer.Images, null)
            ],
            forwards);
    }

    [Fact]
    public void Every_sample_carries_the_date_it_was_taken_for()
    {
        var tally = new StorageTally();
        tally.Add(StorageContainer.Mods, Blob($"{_repoA.Value}/a_mod/1.0.0", 1));

        Assert.All(tally.ToSamples(_date, Registered(), Registered()), x => Assert.Equal(_date, x.Date));
    }


    private static StoredBlob Blob(string name, long length)
    {
        return new StoredBlob(name, DateTimeOffset.UnixEpoch, length, null);
    }

    private static Dictionary<RepoId, long> Registered(params (RepoId RepoId, long Bytes)[] entries)
    {
        return entries.ToDictionary(x => x.RepoId, x => x.Bytes);
    }

    private static StorageUsageSample Single(IReadOnlyList<StorageUsageSample> samples, StorageContainer container, RepoId? repoId)
    {
        return Assert.Single(samples, x => x.Container == container && x.RepoId == repoId);
    }

    private static (long Bytes, int Count) Stored(IReadOnlyList<StorageUsageSample> samples, StorageContainer container, RepoId? repoId)
    {
        var sample = Single(samples, container, repoId);

        return (sample.StoredBytes, sample.BlobCount);
    }

    private static IReadOnlyList<StorageUsageSample> Tally(IEnumerable<(StorageContainer Container, StoredBlob Blob)> blobs)
    {
        var tally = new StorageTally();

        foreach (var (container, blob) in blobs)
        {
            tally.Add(container, blob);
        }

        return tally.ToSamples(_date, Registered(), Registered());
    }

    private static List<(StorageContainer, RepoId?)> Keys(IReadOnlyList<StorageUsageSample> samples)
    {
        return [.. samples.Select(x => (x.Container, x.RepoId))];
    }
}
