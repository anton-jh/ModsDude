using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImageCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"modsdude-image-cache-{Guid.NewGuid():N}");
    private readonly ImageCacheSettings _settings;


    public ModImageCacheTests()
    {
        _settings = new ImageCacheSettings() { Path = _directory, MaxSizeBytes = 1000 };
    }


    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }


    [Fact]
    public async Task What_went_in_comes_back_out()
    {
        var cache = CreateCache();

        await cache.WriteAsync("a-hash", [1, 2, 3], CancellationToken.None);

        Assert.Equal<byte[]>([1, 2, 3], await cache.TryReadAsync("a-hash", CancellationToken.None));
    }

    [Fact]
    public async Task A_key_that_was_never_written_reads_as_a_miss()
    {
        var cache = CreateCache();

        Assert.Null(await cache.TryReadAsync("never-written", CancellationToken.None));
    }

    [Fact]
    public async Task Two_keys_do_not_share_an_entry()
    {
        var cache = CreateCache();

        await cache.WriteAsync("one", [1], CancellationToken.None);
        await cache.WriteAsync("two", [2], CancellationToken.None);

        Assert.Equal<byte[]>([1], await cache.TryReadAsync("one", CancellationToken.None));
        Assert.Equal<byte[]>([2], await cache.TryReadAsync("two", CancellationToken.None));
    }

    [Fact]
    public async Task Passing_the_cap_evicts_the_least_recently_used_first()
    {
        var cache = CreateCache();

        // 1,200 bytes into a 1,000 byte cache, with the oldest use first.
        await WriteAged(cache, "oldest", 300, TimeSpan.FromDays(4));
        await WriteAged(cache, "older", 300, TimeSpan.FromDays(3));
        await WriteAged(cache, "recent", 300, TimeSpan.FromDays(2));
        await WriteAged(cache, "newest", 300, TimeSpan.FromDays(1));

        await cache.EvictAsync(CancellationToken.None);

        Assert.Null(await cache.TryReadAsync("oldest", CancellationToken.None));
        Assert.NotNull(await cache.TryReadAsync("older", CancellationToken.None));
        Assert.NotNull(await cache.TryReadAsync("recent", CancellationToken.None));
        Assert.NotNull(await cache.TryReadAsync("newest", CancellationToken.None));
    }

    [Fact]
    public async Task Eviction_leaves_room_rather_than_stopping_at_the_cap()
    {
        var cache = CreateCache();

        // Stopping the moment it fits would mean sweeping the whole directory again on the very
        // next write.
        for (var i = 0; i < 5; i++)
        {
            await WriteAged(cache, $"entry-{i}", 300, TimeSpan.FromDays(10 - i));
        }

        await cache.EvictAsync(CancellationToken.None);

        Assert.True(new DirectoryInfo(_directory).EnumerateFiles("*.img").Sum(x => x.Length) <= 900);
    }

    [Fact]
    public async Task A_cache_under_its_cap_is_left_alone()
    {
        var cache = CreateCache();

        await WriteAged(cache, "small", 100, TimeSpan.FromDays(30));

        await cache.EvictAsync(CancellationToken.None);

        Assert.NotNull(await cache.TryReadAsync("small", CancellationToken.None));
    }


    private ModImageCache CreateCache() => new(() => _settings);

    /// <summary>
    /// Writes an entry and backdates it, since least-recently-used is approximated by last-write
    /// time - Windows does not maintain last-access time, and a cache this hot cannot afford a
    /// metadata write per read.
    /// </summary>
    private async Task WriteAged(ModImageCache cache, string key, int size, TimeSpan age)
    {
        var payload = new byte[size];
        Array.Fill(payload, (byte)key.Length);
        payload[0] = (byte)key[0];
        payload[1] = (byte)key[^1];

        var before = GetFiles();

        await cache.WriteAsync(key, payload, CancellationToken.None);

        var written = GetFiles().Except(before).Single();

        File.SetLastWriteTimeUtc(written, DateTime.UtcNow - age);
    }

    private HashSet<string> GetFiles()
    {
        return Directory.Exists(_directory)
            ? [.. Directory.EnumerateFiles(_directory, "*.img")]
            : [];
    }
}
