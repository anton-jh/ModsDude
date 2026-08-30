using ModsDude.Client.Core.Import;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// One volume's content-addressed store of mod files: <c>{root}/blobs/{hash[0..2]}/{hash}</c>,
/// shared by every repo and instance it serves.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in here records what a file <em>is</em>. The mapping from <c>(repoId, modId, versionId)</c>
/// to a hash lives on the server and arrives with the profile, and that indirection is the entire
/// security property: two repos disagreeing about what <c>modA/v1</c> contains ask for two different
/// addresses, so neither can serve the other's bytes.
/// </para>
/// <para>
/// Which means <b>every lookup here is keyed by hash and nothing else</b>, and <b>every path in
/// verifies</b> - see <see cref="IngestAsync"/>. One unverified write, or one lookup keyed by mod id,
/// and the isolation argument is gone.
/// See docs/07-mod-sync-design.md#cache-isolation.
/// </para>
/// <para>
/// Blobs are deliberately left writable. Marking them read-only would turn an in-place rewrite
/// through a hardlink into a loud failure rather than silent corruption, but it also stops an
/// in-game updater working at all, and which of those a game does is still an open question -
/// docs/07-mod-sync-design.md#hardlink-support-is-an-adapter-property. Until somebody tests it, the
/// adapter's <c>SupportsHardlinks</c> defaulting to false is what carries the safety.
/// </para>
/// </remarks>
public sealed class ContentStore
{
    private const string _blobsDirectory = "blobs";
    private const string _temporaryDirectory = "tmp";
    private const string _quarantineDirectory = "quarantine";

    /// <summary>
    /// Windows does not maintain last-access time by default, so last-write stands in for
    /// last-used. Refreshing it on every hit would cost a metadata write per file per sync, so a hit
    /// only re-stamps an entry that has already gone stale.
    /// </summary>
    private static readonly TimeSpan _timestampRefreshInterval = TimeSpan.FromHours(12);


    public ContentStore(string volumeRoot, string rootPath, long maxSizeBytes)
    {
        VolumeRoot = volumeRoot;
        RootPath = rootPath;
        MaxSizeBytes = maxSizeBytes;
    }


    /// <summary>The volume this store lives on, in the form <see cref="Persistence.ClientSettings"/> keys by.</summary>
    public string VolumeRoot { get; }

    public string RootPath { get; }

    public long MaxSizeBytes { get; }


    public string GetBlobPath(string hash)
    {
        var normalized = Normalize(hash);

        return Path.Combine(RootPath, _blobsDirectory, normalized[..2], normalized);
    }

    public bool Contains(string hash)
    {
        return File.Exists(GetBlobPath(hash));
    }

    /// <summary>The size of the entry, or null when the store does not hold it.</summary>
    public long? GetSize(string hash)
    {
        var info = new FileInfo(GetBlobPath(hash));

        return info.Exists ? info.Length : null;
    }

    public Stream OpenRead(string hash)
    {
        Touch(hash);

        return new FileStream(GetBlobPath(hash), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>
    /// Writes <paramref name="content"/> into the store at <paramref name="expectedHash"/>, hashing
    /// as it streams and refusing anything that does not add up.
    /// </summary>
    /// <remarks>
    /// This check is what makes a store shared between repos safe, so it has no fast path and no way
    /// around it. A hostile member of one repo who declares another repo's hash while uploading
    /// different bytes only breaks their own repo's mod: verification fails here and nothing is
    /// stored. Landing content at an address it does not hash to would take a second preimage of
    /// SHA-256.
    /// </remarks>
    /// <exception cref="ContentVerificationException">The bytes do not hash to the declared address.</exception>
    public async Task<long> IngestAsync(Stream content, string expectedHash, IProgress<long>? bytesWritten, CancellationToken cancellationToken)
    {
        var temporaryPath = GetTemporaryPath();

        try
        {
            var (hash, length) = await WriteAndHashAsync(content, temporaryPath, bytesWritten, cancellationToken);

            if (ModContentHasher.Matches(hash, expectedHash) is false)
            {
                throw new ContentVerificationException(expectedHash, hash);
            }

            Place(temporaryPath, expectedHash);

            return length;
        }
        finally
        {
            Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Takes a file that is already on disk into the store - the uninstall path, where the bytes are
    /// in a mod folder, and the cross-store path, where they are on another disk.
    /// </summary>
    /// <param name="removeSource">
    /// True to move rather than copy. Used when the source is a mod-folder file being uninstalled,
    /// which is on its way out either way.
    /// </param>
    /// <remarks>
    /// Hashed before it lands, exactly like a download. A mod-folder file is a file the user could
    /// have replaced with anything, so taking its word for what it contains would put unverified
    /// bytes at a verified address - which is the one thing the store cannot allow.
    /// </remarks>
    public async Task<long> IngestFileAsync(string sourcePath, string expectedHash, bool removeSource, CancellationToken cancellationToken)
    {
        var hash = await HashFileAsync(sourcePath, cancellationToken);

        if (ModContentHasher.Matches(hash, expectedHash) is false)
        {
            throw new ContentVerificationException(expectedHash, hash);
        }

        var blobPath = GetBlobPath(expectedHash);

        if (File.Exists(blobPath))
        {
            // Somebody put it there while this was hashing. The address decides the content, so
            // there is nothing to reconcile - and overwriting would break every hardlink into it.
            if (removeSource)
            {
                File.Delete(sourcePath);
            }

            return new FileInfo(blobPath).Length;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);

        var length = new FileInfo(sourcePath).Length;

        if (removeSource)
        {
            // Move handles the cross-volume case by copying and deleting, and on the same volume it
            // is a directory operation - which is what makes uninstalling from a hardlink-served
            // disk free.
            File.Move(sourcePath, blobPath);
        }
        else
        {
            File.Copy(sourcePath, blobPath);
        }

        return length;
    }

    /// <summary>
    /// Copies a blob in from another disk's store, hashing it as it goes.
    /// </summary>
    /// <remarks>
    /// Safe for the same reason cross-repo sharing is safe: every store is content-addressed, so a
    /// blob at address <c>H</c> is by construction content that hashes to <c>H</c>, whichever disk
    /// it sits on. The bytes pass through memory anyway, so verifying costs almost nothing - and it
    /// catches an entry that has rotted or been rewritten through a hardlink.
    /// </remarks>
    public async Task<long> CopyFromAsync(ContentStore source, string hash, IProgress<long>? bytesWritten, CancellationToken cancellationToken)
    {
        await using var content = source.OpenRead(hash);

        return await IngestAsync(content, hash, bytesWritten, cancellationToken);
    }

    /// <summary>
    /// Records that an entry was used, so eviction drops the ones nothing has wanted for longest.
    /// </summary>
    /// <remarks>
    /// Skipped for an entry that has more than one name. Under hardlinking the store's blob and the
    /// mod folder's file are the same file, so re-stamping it here would change the modification time
    /// the sync manifest recorded and the next drift check would report a mod nobody touched. Such an
    /// entry is never evicted anyway - it reclaims nothing - so its place in the order does not
    /// matter.
    /// </remarks>
    public void Touch(string hash)
    {
        try
        {
            var path = GetBlobPath(hash);
            var written = File.GetLastWriteTimeUtc(path);

            if (DateTime.UtcNow - written > _timestampRefreshInterval &&
                FileLinks.TryGetLinkCount(path) is 1)
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
        }
        catch (Exception)
        {
            // Losing a touch costs an entry its place in the eviction order and nothing else.
        }
    }

    /// <summary>
    /// Every blob in the store, with what evicting it would actually reclaim.
    /// </summary>
    public IReadOnlyList<ContentStoreEntry> Enumerate()
    {
        var blobs = Path.Combine(RootPath, _blobsDirectory);

        if (Directory.Exists(blobs) is false)
        {
            return [];
        }

        return [.. new DirectoryInfo(blobs)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(x => new ContentStoreEntry(
                x.Name,
                x.FullName,
                x.Length,
                x.LastWriteTimeUtc,
                // Null means the link count could not be read, which is treated as shared: a file
                // that cannot be inspected is not one to delete on a guess.
                FileLinks.TryGetLinkCount(x.FullName) is int links ? links <= 1 : false))];
    }

    /// <summary>
    /// Drops least-recently-used entries until the store is back inside its size limit.
    /// </summary>
    /// <param name="pinned">
    /// Hashes an active profile needs on some disk this store serves. Evicting one would not break
    /// the installation - a hardlinked file survives losing its store name and a copied one holds
    /// its own bytes - but it would guarantee a re-download on the next sync.
    /// </param>
    /// <remarks>
    /// Only entries the store <em>uniquely</em> holds are counted or dropped. An entry hardlinked
    /// into a live mod folder costs no additional bytes, so evicting it reclaims nothing while
    /// costing a future download. The rule covers both store assignments without a special case: on
    /// a copy-served disk every entry is a standalone file, so all of them count.
    /// </remarks>
    public ContentStoreEvictionResult Evict(IReadOnlySet<string> pinned, CancellationToken cancellationToken)
    {
        var reclaimable = Enumerate().Where(x => x.IsUniquelyHeld).ToList();
        var total = reclaimable.Sum(x => x.Length);

        if (total <= MaxSizeBytes)
        {
            return new ContentStoreEvictionResult(0, 0, total);
        }

        var evicted = 0;
        long reclaimed = 0;

        foreach (var entry in reclaimable.OrderBy(x => x.LastUsedUtc))
        {
            if (total <= MaxSizeBytes)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (pinned.Contains(entry.Hash))
            {
                continue;
            }

            try
            {
                File.Delete(entry.Path);

                total -= entry.Length;
                reclaimed += entry.Length;
                evicted++;
            }
            catch (Exception)
            {
                // Something else is reading it. It will be swept next time; everything in a store is
                // registered somewhere and therefore re-downloadable, so nothing here needs asking.
            }
        }

        return new ContentStoreEvictionResult(evicted, reclaimed, total);
    }

    /// <summary>
    /// Where a file goes when it cannot be recycled - a drive with the Recycle Bin turned off, or a
    /// network path. Timestamped per sync run, so one run's rescued files stay together.
    /// </summary>
    public string GetQuarantineDirectory(DateTimeOffset runStartedAt)
    {
        return Path.Combine(RootPath, _quarantineDirectory, runStartedAt.ToUnixTimeMilliseconds().ToString());
    }


    /// <summary>The SHA-256 of a file on disk, in the encoding the repo records.</summary>
    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ModContentHasher.ComputeAsync(content, cancellationToken);
    }


    /// <summary>
    /// Moves a verified temporary file to its address. An entry that appeared meanwhile is left
    /// alone rather than overwritten: the content is the same by construction, and replacing the
    /// file would break every hardlink already pointing at it.
    /// </summary>
    private void Place(string temporaryPath, string hash)
    {
        var blobPath = GetBlobPath(hash);

        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);

        if (File.Exists(blobPath))
        {
            return;
        }

        try
        {
            File.Move(temporaryPath, blobPath);
        }
        catch (IOException) when (File.Exists(blobPath))
        {
            // Lost the race with another sync writing the same address.
        }
    }

    private async Task<(string Hash, long Length)> WriteAndHashAsync(
        Stream content,
        string temporaryPath,
        IProgress<long>? bytesWritten,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long length = 0;

        await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous))
        {
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);

                if (read == 0)
                {
                    break;
                }

                digest.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                length += read;
                bytesWritten?.Report(length);
            }
        }

        return (ModContentHasher.Format(digest.GetHashAndReset()), length);
    }

    private string GetTemporaryPath()
    {
        return Path.Combine(RootPath, _temporaryDirectory, $"{Guid.NewGuid():N}.part");
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A leftover temporary file costs disk space until the next sweep, nothing more.
        }
    }

    private static string Normalize(string hash)
    {
        if (hash.Length < 3 || hash.Any(x => char.IsAsciiHexDigit(x) is false))
        {
            throw new ArgumentException($"'{hash}' is not a content address.", nameof(hash));
        }

        // Lower-cased here rather than trusted from the caller, so the same content never lands at
        // two addresses because two servers spelled the same hash differently.
        return hash.ToLowerInvariant();
    }
}


/// <param name="IsUniquelyHeld">
/// Whether this store holds the only name for these bytes. False for anything hardlinked into a mod
/// folder, where deleting the store's name reclaims nothing.
/// </param>
public sealed record ContentStoreEntry(string Hash, string Path, long Length, DateTime LastUsedUtc, bool IsUniquelyHeld);

/// <param name="RemainingBytes">What the store uniquely holds afterwards - the number the limit is about.</param>
public sealed record ContentStoreEvictionResult(int EntriesEvicted, long BytesReclaimed, long RemainingBytes);


/// <summary>
/// Bytes that do not hash to the address they were offered under. Never stored, never installed.
/// </summary>
public sealed class ContentVerificationException(string expected, string actual)
    : Exception($"Content offered as '{expected}' hashes to '{actual}'. It was not stored.")
{
    public string ExpectedHash { get; } = expected;
    public string ActualHash { get; } = actual;
}
