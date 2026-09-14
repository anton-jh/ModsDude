using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// Keeping a store inside its size limit without anybody asking.
/// </summary>
/// <remarks>
/// <para>
/// The sweep at the end of an apply covers the ordinary case, but it only ever touches the store that
/// apply used - so a store nothing serves is never visited at all, an import seeds bytes with no sync
/// behind it, and a limit somebody lowered does nothing until the next apply happens to that disk.
/// This is the pass that closes those, and the first of them is what these tests are mostly about:
/// the orphaned store is the one that quietly holds a whole uninstalled game.
/// </para>
/// <para>
/// It used to be a button labelled "Sweep to limit", which is the same work offered as a chore.
/// </para>
/// </remarks>
public class ContentStoreAutoTrimTests
{
    private static readonly ModTargetRef _target = Keys.Target();


    [Fact]
    public async Task A_store_over_its_limit_is_trimmed_without_being_asked()
    {
        using var root = new TempDirectory("trim-over");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), maxSizeBytes: 1);

        await Fill(store, "one", "two", "three");

        var maintenance = Build(root, store);

        var reclaimed = await maintenance.SweepAllAsync(CancellationToken.None);

        Assert.True(reclaimed > 0);
        Assert.Equal(0, store.Measure().Entries);
    }

    [Fact]
    public async Task A_store_inside_its_limit_is_left_alone()
    {
        using var root = new TempDirectory("trim-under");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), maxSizeBytes: 1024L * 1024);

        await Fill(store, "one", "two", "three");

        var maintenance = Build(root, store);

        Assert.Equal(0, await maintenance.SweepAllAsync(CancellationToken.None));
        Assert.Equal(3, store.Measure().Entries);
    }

    /// <summary>
    /// The gap the button never really covered either: a store that no longer serves any mod folder
    /// is never reached by a sync's own sweep, and it is exactly the store holding an uninstalled
    /// game's whole cache.
    /// </summary>
    [Fact]
    public async Task A_store_that_serves_no_mod_folder_is_still_trimmed()
    {
        using var root = new TempDirectory("trim-orphan");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), maxSizeBytes: 1);

        await Fill(store, "one", "two");

        // No mod folders at all, which is what an uninstalled game leaves behind.
        var maintenance = new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(),
            new SyncManifestStore(root.CreateSubdirectory("manifests")),
            new ResourceLeases(),
            NullLogger<ContentStoreMaintenance>.Instance);

        Assert.True(await maintenance.SweepAllAsync(CancellationToken.None) > 0);
        Assert.Equal(0, store.Measure().Entries);
    }

    /// <summary>
    /// What an installed profile is running is off limits, exactly as it is for the sync's own sweep -
    /// dropping it would not break the folder but would guarantee a re-download, which is the opposite
    /// of what tidying is for.
    /// </summary>
    [Fact]
    public async Task Trimming_spares_what_an_installed_profile_is_running()
    {
        using var root = new TempDirectory("trim-pinned");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), maxSizeBytes: 1);
        var modFolder = root.CreateSubdirectory("mods");
        var manifests = new SyncManifestStore(root.CreateSubdirectory("manifests"));

        var hashes = await Fill(store, "kept", "dropped");

        manifests.Write(new SyncManifest
        {
            Target = _target,
            RepoId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            ProfileName = "Season 4",
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = modFolder,
            Entries = [new SyncManifestEntry("mod-a", "v1", hashes[0], "fs25_a.zip", 4, DateTimeOffset.UtcNow)]
        });

        var maintenance = new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(new GameModFolder(_target, modFolder)),
            manifests,
            new ResourceLeases(),
            NullLogger<ContentStoreMaintenance>.Instance);

        await maintenance.SweepAllAsync(CancellationToken.None);

        Assert.True(store.Contains(hashes[0]));
        Assert.False(store.Contains(hashes[1]));
    }

    /// <summary>
    /// Housekeeping nobody asked for gives way to work somebody did. The next trigger - startup, the
    /// next import, the next saved settings page - picks it up, and everything in a store is
    /// re-downloadable in any case.
    /// </summary>
    [Fact]
    public async Task A_busy_store_is_skipped_rather_than_waited_for()
    {
        using var root = new TempDirectory("trim-busy");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), maxSizeBytes: 1);

        await Fill(store, "one", "two");

        var leases = new ResourceLeases();

        using var apply = await leases.AcquireSharedAsync(
            [ResourceKeys.Store(store)], "an apply installing from it", CancellationToken.None);

        var maintenance = new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(),
            new SyncManifestStore(root.CreateSubdirectory("manifests")),
            leases,
            NullLogger<ContentStoreMaintenance>.Instance);

        // Returns rather than blocking, and leaves the store as it found it.
        Assert.Equal(0, await maintenance.SweepAllAsync(CancellationToken.None));
        Assert.Equal(2, store.Measure().Entries);
    }

    [Fact]
    public async Task Trimming_releases_the_store_it_claimed()
    {
        using var root = new TempDirectory("trim-release");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), maxSizeBytes: 1);

        await Fill(store, "one");

        var leases = new ResourceLeases();

        var maintenance = new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(),
            new SyncManifestStore(root.CreateSubdirectory("manifests")),
            leases,
            NullLogger<ContentStoreMaintenance>.Instance);

        await maintenance.SweepAllAsync(CancellationToken.None);

        Assert.False(leases.IsHeld(ResourceKeys.Store(store)));
    }


    private static ContentStoreMaintenance Build(TempDirectory root, ContentStore store)
    {
        return new ContentStoreMaintenance(
            new FakeStoreProvider(store),
            new FakeModFolders(new GameModFolder(_target, root.CreateSubdirectory("mods"))),
            new SyncManifestStore(root.CreateSubdirectory("manifests")),
            new ResourceLeases(),
            NullLogger<ContentStoreMaintenance>.Instance);
    }

    private static async Task<string[]> Fill(ContentStore store, params string[] contents)
    {
        var hashes = new List<string>();

        foreach (var content in contents)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = ModContentHasher.Format(SHA256.HashData(bytes));

            await store.IngestAsync(new MemoryStream(bytes), hash, null, CancellationToken.None);
            hashes.Add(hash);
        }

        return [.. hashes];
    }
}
