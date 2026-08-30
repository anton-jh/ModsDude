using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class ContentStoreTests
{
    private const long _oneGigabyte = 1024L * 1024 * 1024;


    [Fact]
    public async Task Ingest_stores_the_bytes_at_their_own_address()
    {
        using var root = new TempDirectory("store-ingest");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var content = Bytes("a mod archive");
        var hash = HashOf(content);

        await store.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);

        Assert.True(store.Contains(hash));
        Assert.Equal(content, await File.ReadAllBytesAsync(store.GetBlobPath(hash)));

        // The two-character prefix keeps directory sizes sane; thousands of versions in one flat
        // directory is a filesystem hazard on Windows.
        Assert.Equal(
            Path.Combine(root.Path, "blobs", hash[..2], hash),
            store.GetBlobPath(hash));
    }

    [Fact]
    public async Task Ingest_refuses_bytes_that_do_not_hash_to_the_declared_address()
    {
        using var root = new TempDirectory("store-verify");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var declared = HashOf(Bytes("what the repo says is there"));

        var exception = await Assert.ThrowsAsync<ContentVerificationException>(
            () => store.IngestAsync(new MemoryStream(Bytes("hostile bytes")), declared, null, CancellationToken.None));

        Assert.Equal(declared, exception.ExpectedHash);
        Assert.NotEqual(declared, exception.ActualHash);

        // Nothing landed - not at the declared address, and not left behind as a temporary file
        // either. This single check is what makes a store shared between repos safe.
        Assert.False(store.Contains(declared));
        Assert.False(store.Contains(exception.ActualHash));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Ingesting_a_file_verifies_it_before_taking_it()
    {
        using var root = new TempDirectory("store-ingest-file");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var path = root.WriteFile("mods/fs25_a.zip", "the user's own bytes");
        var declared = HashOf(Bytes("something else entirely"));

        await Assert.ThrowsAsync<ContentVerificationException>(
            () => store.IngestFileAsync(path, declared, removeSource: true, CancellationToken.None));

        // A mod-folder file is a file the user could have replaced with anything, so a failed check
        // must leave it where it is rather than consuming it.
        Assert.True(File.Exists(path));
        Assert.False(store.Contains(declared));
    }

    [Fact]
    public async Task Ingesting_a_file_with_removeSource_moves_it()
    {
        using var root = new TempDirectory("store-move");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);

        var path = root.WriteFile("mods/fs25_a.zip", "uninstalled mod");
        var hash = HashOf(Bytes("uninstalled mod"));

        await store.IngestFileAsync(path, hash, removeSource: true, CancellationToken.None);

        Assert.False(File.Exists(path));
        Assert.True(store.Contains(hash));
    }

    [Fact]
    public async Task Copying_from_another_store_verifies_as_it_streams()
    {
        using var source = new TempDirectory("store-source");
        using var target = new TempDirectory("store-target");

        var from = new ContentStore("D:\\", source.Path, _oneGigabyte);
        var to = new ContentStore("C:\\", target.Path, _oneGigabyte);

        var content = Bytes("a mod both disks want");
        var hash = HashOf(content);

        await from.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);
        await to.CopyFromAsync(from, hash, null, CancellationToken.None);

        Assert.True(to.Contains(hash));

        // A store entry that has rotted, or been rewritten through a hardlink, is caught on the way
        // across rather than installed.
        File.Delete(to.GetBlobPath(hash));
        await File.WriteAllTextAsync(from.GetBlobPath(hash), "rewritten underneath us");

        await Assert.ThrowsAsync<ContentVerificationException>(
            () => to.CopyFromAsync(from, hash, null, CancellationToken.None));

        Assert.False(to.Contains(hash));
    }

    [Fact]
    public async Task An_entry_hardlinked_into_a_mod_folder_is_not_uniquely_held()
    {
        using var root = new TempDirectory("store-links");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);
        var modFolder = root.CreateSubdirectory("mods");

        var content = Bytes("an installed mod");
        var hash = HashOf(content);

        await store.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);

        var installed = Path.Combine(modFolder, "fs25_a.zip");
        var linked = FileLinks.TryCreateHardLink(installed, store.GetBlobPath(hash));

        Assert.True(linked,
            $"Could not create a hardlink under '{root.Path}'. The temp directory is not on a filesystem " +
            "that supports them, so store accounting cannot be verified here.");

        var entry = Assert.Single(store.Enumerate());

        Assert.Equal(hash, entry.Hash);
        Assert.False(entry.IsUniquelyHeld);

        File.Delete(installed);

        // The store's own name still points at the data - which is what makes uninstalling from a
        // hardlink-served disk free.
        Assert.True(store.Contains(hash));
        Assert.True(Assert.Single(store.Enumerate()).IsUniquelyHeld);
    }

    [Fact]
    public async Task Eviction_counts_only_what_the_store_uniquely_holds()
    {
        using var root = new TempDirectory("store-eviction-accounting");
        var modFolder = root.CreateSubdirectory("mods");

        // A limit of ten bytes against two entries of well over that: whether anything is dropped
        // comes down entirely to which of them the accounting counts.
        var store = new ContentStore("C:\\", root.Path, maxSizeBytes: 10);

        var installedHash = await Store(store, "this one is installed and hardlinked");
        var idleHash = await Store(store, "this one is only in the store");

        Assert.True(
            FileLinks.TryCreateHardLink(Path.Combine(modFolder, "fs25_a.zip"), store.GetBlobPath(installedHash)),
            $"Could not create a hardlink under '{root.Path}'; the eviction rule cannot be verified here.");

        var result = store.Evict(new HashSet<string>(), CancellationToken.None);

        // Only the idle entry counted, and only the idle entry went: evicting a hardlinked entry
        // reclaims nothing, because the mod folder still names the same bytes.
        Assert.Equal(1, result.EntriesEvicted);
        Assert.True(store.Contains(installedHash));
        Assert.False(store.Contains(idleHash));
    }

    [Fact]
    public async Task Eviction_drops_the_least_recently_used_first_and_never_what_a_profile_needs()
    {
        using var root = new TempDirectory("store-eviction-order");
        var store = new ContentStore("C:\\", root.Path, maxSizeBytes: 40);

        var oldest = await Store(store, "aaaaaaaaaaaaaaaaaaaa");
        var middle = await Store(store, "bbbbbbbbbbbbbbbbbbbb");
        var newest = await Store(store, "cccccccccccccccccccc");

        Age(store, oldest, TimeSpan.FromDays(30));
        Age(store, middle, TimeSpan.FromDays(20));
        Age(store, newest, TimeSpan.FromDays(1));

        var result = store.Evict(new HashSet<string>(), CancellationToken.None);

        Assert.Equal(1, result.EntriesEvicted);
        Assert.False(store.Contains(oldest));
        Assert.True(store.Contains(middle));
        Assert.True(store.Contains(newest));

        // Pinning what an active profile needs: the sweep skips it and takes the next oldest, since
        // evicting it would guarantee a re-download on the very next sync.
        var tighter = new ContentStore("C:\\", root.Path, maxSizeBytes: 20);

        Age(store, middle, TimeSpan.FromDays(20));
        tighter.Evict(new HashSet<string>([middle], StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.True(store.Contains(middle));
        Assert.False(store.Contains(newest));
    }

    [Fact]
    public async Task Ingest_leaves_an_existing_entry_alone()
    {
        using var root = new TempDirectory("store-existing");
        var store = new ContentStore("C:\\", root.Path, _oneGigabyte);
        var modFolder = root.CreateSubdirectory("mods");

        var content = Bytes("shared by two repos");
        var hash = HashOf(content);

        await store.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);

        var installed = Path.Combine(modFolder, "fs25_a.zip");

        Assert.True(
            FileLinks.TryCreateHardLink(installed, store.GetBlobPath(hash)),
            $"Could not create a hardlink under '{root.Path}'.");

        await store.IngestAsync(new MemoryStream(content), hash, null, CancellationToken.None);

        // Replacing the file would break every hardlink already pointing at it, and the content is
        // the same by construction, so there is nothing to gain by writing.
        Assert.Equal(2, FileLinks.TryGetLinkCount(store.GetBlobPath(hash)));
    }


    private static async Task<string> Store(ContentStore store, string content)
    {
        var bytes = Bytes(content);
        var hash = HashOf(bytes);

        await store.IngestAsync(new MemoryStream(bytes), hash, null, CancellationToken.None);

        return hash;
    }

    private static void Age(ContentStore store, string hash, TimeSpan by)
        => File.SetLastWriteTimeUtc(store.GetBlobPath(hash), DateTime.UtcNow - by);

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    private static string HashOf(byte[] content) => ModContentHasher.Format(SHA256.HashData(content));
}
