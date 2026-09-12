using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// The rewritten-blob check, against a real filesystem with real hardlinks.
/// </summary>
/// <remarks>
/// Every case here turns on file identity, which is the one thing a fake filesystem could not have
/// told us honestly - so these run on the real one, like the rest of the store tests. The two that
/// matter are a hair apart on disk and a world apart in consequence: an updater that renames over
/// leaves the store holding the bytes it always held, and one that writes in place leaves it holding
/// somebody else's, under an address every repo on the volume trusts.
/// </remarks>
public class StoreIntegrityServiceTests
{
    private const long _oneGigabyte = 1024L * 1024 * 1024;

    private static readonly GameIdentity _game = Keys.Game();


    [Fact]
    public async Task An_in_place_rewrite_through_a_hardlink_is_caught_and_the_blob_dropped()
    {
        using var root = new TempDirectory("integrity-rewrite");
        var (store, modFolder, service) = Setup(root, out var manifests);

        var original = Bytes("the version the repo registered");
        var hash = HashOf(original);

        await store.IngestAsync(new MemoryStream(original), hash, null, CancellationToken.None);

        var installed = Path.Combine(modFolder, "FS22_Mod.zip");

        Assert.True(FileLinks.TryCreateHardLink(installed, store.GetBlobPath(hash)), "the test needs a real hardlink");

        manifests.Write(Manifest(modFolder, hash, "FS22_Mod.zip", new FileInfo(installed)));

        // What an in-game updater must never do: open the installed file and write through it. Under
        // hardlinking that file *is* the blob, so this lands in the shared store.
        await File.WriteAllBytesAsync(installed, Bytes("what the game downloaded instead"));

        var found = await service.CheckAsync(_game, modFolder, ["FS22_Mod.zip"], CancellationToken.None);

        var blob = Assert.Single(found);
        Assert.Equal(hash, blob.Hash);
        Assert.Equal("FS22_Mod.zip", blob.FileName);
        Assert.True(blob.Removed);

        // The store no longer serves the wrong bytes under a trusted address, and the user keeps the
        // file the game gave them. That second half is the whole reason dropping the blob is safe.
        Assert.False(store.Contains(hash));
        Assert.Equal(Bytes("what the game downloaded instead"), await File.ReadAllBytesAsync(installed));
    }

    [Fact]
    public async Task A_rename_over_leaves_the_store_alone()
    {
        using var root = new TempDirectory("integrity-rename");
        var (store, modFolder, service) = Setup(root, out var manifests);

        var original = Bytes("the version the repo registered");
        var hash = HashOf(original);

        await store.IngestAsync(new MemoryStream(original), hash, null, CancellationToken.None);

        var installed = Path.Combine(modFolder, "FS22_Mod.zip");

        Assert.True(FileLinks.TryCreateHardLink(installed, store.GetBlobPath(hash)));

        manifests.Write(Manifest(modFolder, hash, "FS22_Mod.zip", new FileInfo(installed)));

        // What Farming Simulator actually does, and why hardlinking is switched on: a new file,
        // renamed over the old name. The blob loses a name and keeps every byte.
        var staged = Path.Combine(modFolder, "FS22_Mod.zip.tmp");
        await File.WriteAllBytesAsync(staged, Bytes("a newer build, downloaded in the game"));
        File.Move(staged, installed, overwrite: true);

        var found = await service.CheckAsync(_game, modFolder, ["FS22_Mod.zip"], CancellationToken.None);

        // Drift, yes - the folder no longer matches. Corruption, no. Nothing was read to establish
        // that: the file in the folder is simply not the blob any more.
        Assert.Empty(found);
        Assert.True(store.Contains(hash));
        Assert.Equal(original, await File.ReadAllBytesAsync(store.GetBlobPath(hash)));
    }

    [Fact]
    public async Task A_copy_served_folder_has_nothing_to_corrupt()
    {
        using var root = new TempDirectory("integrity-copy");
        var (store, modFolder, service) = Setup(root, out var manifests);

        var original = Bytes("the version the repo registered");
        var hash = HashOf(original);

        await store.IngestAsync(new MemoryStream(original), hash, null, CancellationToken.None);

        // No link: its own bytes, which is what a cross-disk assignment or an adapter without
        // hardlink support gets.
        var installed = Path.Combine(modFolder, "FS22_Mod.zip");
        File.Copy(store.GetBlobPath(hash), installed);

        manifests.Write(Manifest(modFolder, hash, "FS22_Mod.zip", new FileInfo(installed)));

        await File.WriteAllBytesAsync(installed, Bytes("rewritten in place, harmlessly"));

        Assert.Empty(await service.CheckAsync(_game, modFolder, ["FS22_Mod.zip"], CancellationToken.None));
        Assert.True(store.Contains(hash));
    }

    [Fact]
    public async Task A_blob_whose_timestamp_moved_but_whose_bytes_did_not_is_left_alone()
    {
        using var root = new TempDirectory("integrity-touched");
        var (store, modFolder, service) = Setup(root, out var manifests);

        var original = Bytes("the version the repo registered");
        var hash = HashOf(original);

        await store.IngestAsync(new MemoryStream(original), hash, null, CancellationToken.None);

        var installed = Path.Combine(modFolder, "FS22_Mod.zip");

        Assert.True(FileLinks.TryCreateHardLink(installed, store.GetBlobPath(hash)));

        manifests.Write(Manifest(modFolder, hash, "FS22_Mod.zip", new FileInfo(installed)));

        // The identity check says "written through", and it is wrong - something stamped the file
        // without changing it. The hash is what stops a blob being deleted on that suspicion alone.
        File.SetLastWriteTimeUtc(installed, DateTime.UtcNow.AddHours(1));

        Assert.Empty(await service.CheckAsync(_game, modFolder, ["FS22_Mod.zip"], CancellationToken.None));
        Assert.True(store.Contains(hash));
    }

    [Fact]
    public async Task A_changed_file_the_store_does_not_hold_is_not_read()
    {
        using var root = new TempDirectory("integrity-absent");
        var (store, modFolder, service) = Setup(root, out var manifests);

        var hash = HashOf(Bytes("evicted since the sync"));
        var installed = Path.Combine(modFolder, "FS22_Mod.zip");

        await File.WriteAllBytesAsync(installed, Bytes("whatever is there now"));

        manifests.Write(Manifest(modFolder, hash, "FS22_Mod.zip", new FileInfo(installed)));

        Assert.Empty(await service.CheckAsync(_game, modFolder, ["FS22_Mod.zip"], CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_changed_means_nothing_is_looked_at()
    {
        using var root = new TempDirectory("integrity-quiet");
        var (_, modFolder, service) = Setup(root, out _);

        Assert.Empty(await service.CheckAsync(_game, modFolder, [], CancellationToken.None));
    }

    [Fact]
    public async Task Verifying_a_blob_answers_for_the_address_it_is_filed_under()
    {
        using var root = new TempDirectory("integrity-verify");
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), _oneGigabyte);

        var content = Bytes("a mod archive");
        var hash = HashOf(content);

        Assert.Equal(BlobVerification.Absent, await store.VerifyAsync(hash, CancellationToken.None));

        await store.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);

        Assert.Equal(BlobVerification.Intact, await store.VerifyAsync(hash, CancellationToken.None));

        // Only reachable by writing the blob behind the store's back, which is exactly what an
        // in-place rewrite through a hardlink is.
        await File.WriteAllBytesAsync(store.GetBlobPath(hash), Bytes("something else entirely"));

        Assert.Equal(BlobVerification.Corrupt, await store.VerifyAsync(hash, CancellationToken.None));

        Assert.True(store.Remove(hash));
        Assert.False(store.Contains(hash));

        // Removing what is not there is an answer, not a failure.
        Assert.False(store.Remove(hash));
    }

    [Fact]
    public void Two_names_of_one_file_share_an_identity_and_two_copies_do_not()
    {
        using var root = new TempDirectory("identity");

        var first = root.WriteFile("first.zip", "identical bytes");
        var link = root.Combine("link.zip");
        var copy = root.Combine("copy.zip");

        Assert.True(FileLinks.TryCreateHardLink(link, first));
        File.Copy(first, copy);

        var identity = FileLinks.TryGetFileIdentity(first);

        Assert.NotNull(identity);
        Assert.Equal(identity, FileLinks.TryGetFileIdentity(link));

        // The point of using identity rather than content: these two files are byte-for-byte equal
        // and writing to one cannot touch the other.
        Assert.NotEqual(identity, FileLinks.TryGetFileIdentity(copy));

        Assert.Null(FileLinks.TryGetFileIdentity(root.Combine("never-existed.zip")));
    }


    private static (ContentStore Store, string ModFolder, StoreIntegrityService Service) Setup(
        TempDirectory root,
        out SyncManifestStore manifests)
    {
        var store = new ContentStore("C:\\", root.CreateSubdirectory("store"), _oneGigabyte);
        var modFolder = root.CreateSubdirectory("mods");

        manifests = new SyncManifestStore(root.CreateSubdirectory("manifests"));

        return (store, modFolder, new StoreIntegrityService(
            new FakeStoreProvider(store),
            manifests,
            NullLogger<StoreIntegrityService>.Instance));
    }

    private static SyncManifest Manifest(string modFolder, string hash, string fileName, FileInfo installed) => new()
    {
        Game = _game,
        RepoId = Guid.NewGuid(),
        ProfileId = Guid.NewGuid(),
        SyncedAt = DateTimeOffset.UtcNow,
        ModFolder = modFolder,
        Entries =
        [
            new SyncManifestEntry("mod-a", "v1", hash, fileName, installed.Length, installed.LastWriteTimeUtc)
            {
                DisplayName = "A Mod"
            }
        ]
    };

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    private static string HashOf(byte[] content) => ModContentHasher.Format(SHA256.HashData(content));
}
