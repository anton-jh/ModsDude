using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// The exhaustive pass a settings page offers, as opposed to the cheap one that rides along with
/// drift.
/// </summary>
/// <remarks>
/// The two answer different questions. The drift-driven check only ever looks at files a mod folder
/// reported changed, so it is blind to a blob nothing was watching; this one reads everything, which
/// is why it is a button rather than a background job.
/// </remarks>
public class ContentStoreVerificationTests
{
    private const long _oneGigabyte = 1024L * 1024 * 1024;

    private static readonly GameIdentity _game = Keys.Game();


    [Fact]
    public async Task A_healthy_store_verifies_clean_and_loses_nothing()
    {
        using var root = new TempDirectory("verify-clean");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var hashes = await Fill(store, "one", "two", "three");

        var result = await store.VerifyAllAsync(null, CancellationToken.None);

        Assert.Equal(3, result.Checked);
        Assert.False(result.FoundProblems);
        Assert.Empty(result.Corrupt);
        Assert.Equal(0, result.Unreadable);
        Assert.All(hashes, x => Assert.True(store.Contains(x)));
    }

    [Fact]
    public async Task A_blob_rewritten_behind_the_stores_back_is_found_and_dropped()
    {
        using var root = new TempDirectory("verify-rot");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var hashes = await Fill(store, "one", "two", "three");

        // No mod folder involved, which is the point: a failing disk or a stray tool leaves no trail
        // for the drift check to follow, and this is the only thing that catches it.
        await File.WriteAllBytesAsync(store.GetBlobPath(hashes[1]), Bytes("rot"));

        var result = await store.VerifyAllAsync(null, CancellationToken.None);

        Assert.Equal(3, result.Checked);
        Assert.True(result.FoundProblems);
        Assert.Equal([hashes[1]], result.Corrupt);
        Assert.Equal(1, result.Removed);

        Assert.False(store.Contains(hashes[1]));

        // Its neighbours are untouched. A verification pass that took out the whole store on one bad
        // file would be a worse outcome than the bad file.
        Assert.True(store.Contains(hashes[0]));
        Assert.True(store.Contains(hashes[2]));
    }

    [Fact]
    public async Task A_name_that_is_not_a_content_address_is_reported_rather_than_deleted()
    {
        using var root = new TempDirectory("verify-junk");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        await Fill(store, "one");

        var junk = Path.Combine(root.Path, "blobs", "zz", "not-a-hash.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(junk)!);
        await File.WriteAllTextAsync(junk, "something put this here");

        var result = await store.VerifyAllAsync(null, CancellationToken.None);

        // Unreadable, not corrupt - and still on disk. This is not the code that knows what put it
        // there, and a verification pass is no place to start deleting files it does not understand.
        Assert.Equal(1, result.Unreadable);
        Assert.Empty(result.Corrupt);
        Assert.True(File.Exists(junk));
    }

    [Fact]
    public async Task Progress_counts_both_files_and_bytes()
    {
        using var root = new TempDirectory("verify-progress");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        // Deliberately different sizes: on a real store one mod is 4 KB and the next is 400 MB, so a
        // file count alone would make progress lurch and a byte count alone would hide a stall.
        await Fill(store, "a", "bb", "cccc");

        var progress = new ProgressCollector();

        var result = await store.VerifyAllAsync(progress, CancellationToken.None);

        Assert.Equal(3, progress.Reports.Count);
        Assert.Equal([1, 2, 3], progress.Reports.Select(x => x.Checked));
        Assert.All(progress.Reports, x => Assert.Equal(3, x.Total));

        // Bytes climb, and land on the total that was fixed up front. Not asserted against a literal
        // sequence: the walk is ordered by hash prefix, so which of the three comes first is a fact
        // about SHA-256 rather than about the store.
        Assert.Equal(
            progress.Reports.Select(x => x.BytesRead).Order(),
            progress.Reports.Select(x => x.BytesRead));

        Assert.All(progress.Reports, x => Assert.Equal(7, x.TotalBytes));
        Assert.Equal(7, progress.Reports[^1].BytesRead);
        Assert.Equal(7, result.BytesRead);
    }

    [Fact]
    public async Task Stopping_part_way_keeps_what_it_already_repaired()
    {
        using var root = new TempDirectory("verify-cancel");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var hashes = await Fill(store, "one", "two", "three");

        // All three are bad, so whichever the walk reaches first is one it must deal with before the
        // cancellation lands.
        foreach (var hash in hashes)
        {
            await File.WriteAllBytesAsync(store.GetBlobPath(hash), Bytes("rot"));
        }

        using var cancellation = new CancellationTokenSource();
        var progress = new ProgressCollector(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.VerifyAllAsync(progress, cancellation.Token));

        // Exactly one entry was reached, and dropping it stuck. A stopped pass is a partial pass,
        // not an undone one - which is what lets the settings page offer Stop without a caveat.
        Assert.Single(progress.Reports);
        Assert.Equal(2, store.Enumerate().Count);
    }

    /// <summary>
    /// The half the store cannot answer: dropping a bad blob does not repair a mod folder hardlinked
    /// to it, so somebody has to be told which folders to re-apply.
    /// </summary>
    [Fact]
    public async Task Verification_names_the_mod_folders_still_running_a_bad_file()
    {
        using var root = new TempDirectory("verify-affected");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), _oneGigabyte);
        var modFolder = root.CreateSubdirectory("mods");
        var manifests = new SyncManifestStore(root.CreateSubdirectory("manifests"));

        var hashes = await Fill(store, "one", "two");

        manifests.Write(new SyncManifest
        {
            Game = _game,
            RepoId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            ProfileName = "Season 4",
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = modFolder,
            Entries =
            [
                new SyncManifestEntry("mod-a", "v1", hashes[0], "fs25_a.zip", 3, DateTimeOffset.UtcNow),
                new SyncManifestEntry("mod-b", "v1", hashes[1], "fs25_b.zip", 3, DateTimeOffset.UtcNow)
            ]
        });

        var maintenance = new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(new GameModFolder(_game, modFolder)),
            manifests,
            NullLogger<ContentStoreMaintenance>.Instance);

        await File.WriteAllBytesAsync(store.GetBlobPath(hashes[0]), Bytes("rot"));

        var report = await maintenance.VerifyAsync(store, null, CancellationToken.None);

        Assert.Single(report.Result.Corrupt);

        var affected = Assert.Single(report.Affected);

        Assert.Equal(modFolder, affected.ModFolder);
        Assert.Equal("Season 4", affected.ProfileName);
        Assert.Equal(1, affected.Mods);
    }

    [Fact]
    public async Task A_bad_file_nobody_is_running_needs_nothing_further()
    {
        using var root = new TempDirectory("verify-unaffected");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), _oneGigabyte);
        var modFolder = root.CreateSubdirectory("mods");
        var manifests = new SyncManifestStore(root.CreateSubdirectory("manifests"));

        var hashes = await Fill(store, "one");

        var maintenance = new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(new GameModFolder(_game, modFolder)),
            manifests,
            NullLogger<ContentStoreMaintenance>.Instance);

        await File.WriteAllBytesAsync(store.GetBlobPath(hashes[0]), Bytes("rot"));

        var report = await maintenance.VerifyAsync(store, null, CancellationToken.None);

        // Corrupt, dropped, and nothing to re-apply: no manifest names it, so removing it was the
        // whole repair.
        Assert.Single(report.Result.Corrupt);
        Assert.Empty(report.Affected);
    }


    private static async Task<string[]> Fill(ContentStore store, params string[] contents)
    {
        var hashes = new List<string>();

        foreach (var content in contents)
        {
            var bytes = Bytes(content);
            var hash = HashOf(bytes);

            await store.IngestAsync(new MemoryStream(bytes), hash, null, CancellationToken.None);
            hashes.Add(hash);
        }

        return [.. hashes];
    }

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    private static string HashOf(byte[] content) => ModContentHasher.Format(SHA256.HashData(content));


    /// <summary>
    /// A synchronous <see cref="IProgress{T}"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> posts to the thread pool where there is no synchronisation context,
    /// which is exactly right for a UI and useless for asserting on - the reports would arrive after
    /// the assertions.
    /// </remarks>
    private sealed class ProgressCollector(Action<ContentStoreVerificationProgress>? onReport = null)
        : IProgress<ContentStoreVerificationProgress>
    {
        public List<ContentStoreVerificationProgress> Reports { get; } = [];

        public void Report(ContentStoreVerificationProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }
}
