using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public sealed class ContentStoreMoveTests : IDisposable
{
    private const long _oneGigabyte = 1024L * 1024 * 1024;

    private readonly TempDirectory _root = new("store-move");
    private readonly ContentStore _store;
    private readonly string _destination;
    private int _commits;


    public ContentStoreMoveTests()
    {
        _store = new ContentStore("C:\\", _root.Combine("old"), _oneGigabyte);
        _destination = _root.Combine("new");
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Everything_the_store_held_ends_up_in_the_new_folder_and_the_old_one_is_gone(bool sameVolume)
    {
        var hash = await IngestAsync("a mod archive");
        _root.WriteFile("old/quarantine/2026-10-01/rescued.zip", "nobody registered this");

        await Move().RunAsync(sameVolume, CancellationToken.None);

        var moved = new ContentStore("C:\\", _destination, _oneGigabyte);

        Assert.Equal(1, _commits);
        Assert.True(moved.Contains(hash));
        Assert.True(File.Exists(Path.Combine(moved.QuarantinePath, "2026-10-01", "rescued.zip")));
        Assert.False(Directory.Exists(_store.RootPath));
    }

    [Fact]
    public async Task A_copy_leaves_behind_whatever_else_somebody_put_in_the_old_folder()
    {
        await IngestAsync("a mod archive");
        var foreign = _root.WriteFile("old/notes.txt", "not the store's");

        await Move().RunAsync(sameVolume: false, CancellationToken.None);

        Assert.True(File.Exists(foreign));
        Assert.False(_store.IsMarked);
    }

    [Fact]
    public async Task A_copy_keeps_when_each_entry_was_last_used()
    {
        var hash = await IngestAsync("a mod archive");
        var lastUsed = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        _store.SetLastUsed(hash, lastUsed);

        await Move().RunAsync(sameVolume: false, CancellationToken.None);

        Assert.Equal(lastUsed, Assert.Single(new ContentStore("C:\\", _destination, _oneGigabyte).Enumerate()).LastUsedUtc);
    }

    [Fact]
    public async Task A_copy_cancelled_part_way_leaves_the_old_store_as_it_was_and_writes_nothing_down()
    {
        var first = await IngestAsync("first");
        var second = await IngestAsync("second");
        using var cancellation = new CancellationTokenSource();

        var move = new ContentStoreMove(_store, _destination, () => _commits++, new CancelOnFirstReport(cancellation), NullLogger.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move.RunAsync(sameVolume: false, cancellation.Token));

        Assert.Equal(0, _commits);
        Assert.True(_store.Contains(first));
        Assert.True(_store.Contains(second));
        Assert.False(Directory.Exists(_destination));
    }

    [Fact]
    public async Task A_copy_cancelled_part_way_keeps_when_each_old_entry_was_last_used()
    {
        var first = await IngestAsync("first");
        var second = await IngestAsync("second");
        var lastUsed = new DateTime(2024, 10, 14, 11, 58, 0, DateTimeKind.Utc);
        _store.SetLastUsed(first, lastUsed);
        _store.SetLastUsed(second, lastUsed);
        using var cancellation = new CancellationTokenSource();

        var move = new ContentStoreMove(_store, _destination, () => _commits++, new CancelOnFirstReport(cancellation), NullLogger.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move.RunAsync(sameVolume: false, cancellation.Token));

        Assert.All(_store.Enumerate(), x => Assert.Equal(lastUsed, x.LastUsedUtc));
    }

    [Fact]
    public async Task A_commit_that_fails_after_a_copy_leaves_the_old_store_in_place()
    {
        var hash = await IngestAsync("a mod archive");

        var move = new ContentStoreMove(_store, _destination, () => throw new IOException("settings"), null, NullLogger.Instance);

        await Assert.ThrowsAsync<IOException>(() => move.RunAsync(sameVolume: false, CancellationToken.None));

        Assert.True(_store.Contains(hash));
        Assert.False(Directory.Exists(_destination));
    }

    [Fact]
    public async Task A_commit_that_fails_after_a_rename_puts_the_store_back()
    {
        var hash = await IngestAsync("a mod archive");

        var move = new ContentStoreMove(_store, _destination, () => throw new IOException("settings"), null, NullLogger.Instance);

        await Assert.ThrowsAsync<IOException>(() => move.RunAsync(sameVolume: true, CancellationToken.None));

        Assert.True(_store.Contains(hash));
        Assert.False(Directory.Exists(_destination));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_folder_holding_other_files_is_refused_before_anything_moves(bool sameVolume)
    {
        var hash = await IngestAsync("a mod archive");
        var foreign = _root.WriteFile("new/somebody's.txt", "not the store's");

        await Assert.ThrowsAsync<UserFriendlyException>(() => Move().RunAsync(sameVolume, CancellationToken.None));

        Assert.Equal(0, _commits);
        Assert.True(_store.Contains(hash));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public async Task A_folder_inside_the_store_is_refused()
    {
        await IngestAsync("a mod archive");

        var move = new ContentStoreMove(_store, Path.Combine(_store.RootPath, "inner"), () => _commits++, null, NullLogger.Instance);

        await Assert.ThrowsAsync<UserFriendlyException>(() => move.RunAsync(sameVolume: true, CancellationToken.None));

        Assert.Equal(0, _commits);
    }

    [Fact]
    public async Task A_store_with_nothing_in_it_yet_only_writes_the_new_folder_down()
    {
        await Move().RunAsync(sameVolume: false, CancellationToken.None);

        Assert.Equal(1, _commits);
        Assert.False(Directory.Exists(_destination));
    }


    public void Dispose()
    {
        _root.Dispose();
    }


    private ContentStoreMove Move() => new(_store, _destination, () => _commits++, null, NullLogger.Instance);

    private async Task<string> IngestAsync(string text)
    {
        var content = Encoding.UTF8.GetBytes(text);
        var hash = ModContentHasher.Format(SHA256.HashData(content));

        await _store.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);

        return hash;
    }


    private sealed class CancelOnFirstReport(CancellationTokenSource cancellation) : IProgress<ContentStoreMoveProgress>
    {
        public void Report(ContentStoreMoveProgress value) => cancellation.Cancel();
    }
}
