using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// One volume's content-addressed store of mod files: <c>{root}/blobs/{hash[0..2]}/{hash}</c>,
/// shared by every repo and game it serves.
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
/// Blobs are deliberately left writable, and the exposure that opens is answered by <b>detection
/// rather than prevention</b> - see <see cref="VerifyAsync"/>. Marking them read-only would turn an
/// in-place rewrite through a hardlink into a loud failure rather than silent corruption, but on
/// Windows the read-only attribute also blocks unlinking the name, which is exactly the harmless
/// thing an in-game updater does when it renames over a mod file. Everything here is
/// re-downloadable by construction, so catching a rewritten blob promptly costs a download and
/// lands in nearly the same place as preventing one.
/// See docs/07-mod-sync-design.md#detecting-a-rewritten-blob.
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


    public ContentStore(string volumeRoot, string rootPath, long maxSizeBytes, ILogger? logger = null)
    {
        Log = logger ?? NullLogger.Instance;
        VolumeRoot = volumeRoot;
        RootPath = rootPath;
        MaxSizeBytes = maxSizeBytes;
    }


    /// <summary>The volume this store lives on, in the form <see cref="Persistence.ClientSettings"/> keys by.</summary>
    public string VolumeRoot { get; }

    public string RootPath { get; }

    public long MaxSizeBytes { get; }

    /// <summary>Never null: a store built without one is a store nothing is listening to.</summary>
    private ILogger Log { get; }


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
    /// Re-reads a blob and asks whether it still hashes to the address it is filed under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one question a content-addressed store can always answer about itself, and the reason
    /// blobs can be left writable: an entry rewritten in place through a hardlink stops matching its
    /// own name, and nothing else in the system can make that happen.
    /// </para>
    /// <para>
    /// It reads the whole file, so <b>it is never run across a store</b> - a sweep over a full one is
    /// tens of gigabytes. <see cref="StoreIntegrityService"/> narrows to the entries that can
    /// actually have been rewritten first, which is normally none of them.
    /// </para>
    /// </remarks>
    public async Task<BlobVerification> VerifyAsync(string hash, CancellationToken cancellationToken)
    {
        var path = GetBlobPath(hash);

        if (File.Exists(path) is false)
        {
            return BlobVerification.Absent;
        }

        try
        {
            return ModContentHasher.Matches(await HashFileAsync(path, cancellationToken), hash)
                ? BlobVerification.Intact
                : BlobVerification.Corrupt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A blob the game has open exclusively, or a drive pulled mid-read. Unknown, and
            // deliberately not corrupt: the caller's response to corrupt is to delete it.
            Log.LogDebug(exception, "Could not verify the store blob {Hash}.", hash);

            return BlobVerification.Unreadable;
        }
    }

    /// <summary>
    /// Drops one blob by address, whatever its place in the eviction order.
    /// </summary>
    /// <remarks>
    /// For a blob that has failed <see cref="VerifyAsync"/>, where the bytes at that address are
    /// provably not the content the address names. Deleting the store's name is the whole repair:
    /// under hardlinking the mod folder's name for those same bytes survives untouched, so the
    /// user keeps the file the game updated, and the address goes back to being re-downloadable.
    /// </remarks>
    /// <returns>False where the entry was already gone or would not delete.</returns>
    public bool Remove(string hash)
    {
        try
        {
            var path = GetBlobPath(hash);

            if (File.Exists(path) is false)
            {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch (Exception exception)
        {
            // Left in place and reported. It fails verification again on the next check, which is
            // the right amount of persistence for something that costs a download to fix.
            Log.LogWarning(exception, "Could not remove the store blob {Hash}.", hash);

            return false;
        }
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
        catch (Exception exception)
        {
            // Losing a touch costs an entry its place in the eviction order and nothing else.
            Log.LogDebug(exception, "Could not touch the store blob {Hash}.", hash);
        }
    }

    /// <summary>
    /// Re-reads every blob in the store and drops the ones that no longer hash to their own address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exhaustive form of <see cref="VerifyAsync"/>, and <b>the only thing here that catches a
    /// blob nothing was watching</b>. The drift-driven check in <see cref="StoreIntegrityService"/>
    /// only ever looks at files a mod folder reported changed, so a blob damaged by a failing disk or
    /// rewritten by some tool that never touched a mod folder is invisible to it. This looks at all
    /// of them.
    /// </para>
    /// <para>
    /// It costs a full read of the store - tens of gigabytes on a large one - which is why nothing
    /// calls it on a schedule and why it takes a <paramref name="cancellationToken"/> that is
    /// expected to be used. It is a button somebody presses.
    /// </para>
    /// <para>
    /// A corrupt entry is deleted, on the same reasoning as <see cref="Clear"/>: the bytes at that
    /// address are provably not what the address names, so the entry is worse than useless - every
    /// repo on the volume would be served it - and what it should have held is registered in a repo
    /// and downloads again. <b>What this cannot repair is a mod folder</b>: where the entry was
    /// hardlinked, the folder's name for those same bytes is still there and still wrong, and only
    /// re-applying that profile fixes it.
    /// </para>
    /// </remarks>
    public async Task<ContentStoreVerificationResult> VerifyAllAsync(
        IProgress<ContentStoreVerificationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entries = Enumerate();
        var total = entries.Sum(x => x.Length);

        var corrupt = new List<string>();
        var removed = 0;
        var unreadable = 0;
        var done = 0;
        long read = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (await VerifyEntryAsync(entry, cancellationToken))
            {
                case BlobVerification.Corrupt:
                    Log.LogWarning("The store blob {Hash} no longer matches its address. Removing it.", entry.Hash);

                    corrupt.Add(entry.Hash);

                    if (Remove(entry.Hash))
                    {
                        removed++;
                    }

                    break;

                // Absent counts here too: something deleted it between the listing and the read,
                // which is not damage and not worth reporting as any kind of failure.
                case BlobVerification.Unreadable:
                    unreadable++;
                    break;
            }

            done++;
            read += entry.Length;

            progress?.Report(new ContentStoreVerificationProgress(done, entries.Count, read, total));
        }

        return new ContentStoreVerificationResult(done, read, corrupt, removed, unreadable);
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
    /// What the store is currently holding, in the terms the size limit is expressed in.
    /// </summary>
    /// <remarks>
    /// Walks the whole blob tree and reads a link count per file, so it belongs off whatever thread
    /// is drawing - it is a settings page's answer, not something to ask during a sync.
    /// </remarks>
    public ContentStoreUsage Measure()
    {
        var entries = Enumerate();

        return new ContentStoreUsage(
            entries.Count,
            entries.Sum(x => x.Length),
            entries.Where(x => x.IsUniquelyHeld).Sum(x => x.Length),
            MeasureDirectory(Path.Combine(RootPath, _temporaryDirectory)),
            MeasureDirectory(Path.Combine(RootPath, _quarantineDirectory)));
    }

    /// <summary>
    /// Gives back every byte this store is actually costing: the blobs it uniquely holds, and the
    /// temporary files a failed write left behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only what the store uniquely holds, which is the whole point of it.</b> A blob hardlinked
    /// into a live mod folder is one file under two names - deleting the store's name frees not one
    /// byte, because the folder still holds the data - so dropping it buys a guaranteed re-download
    /// for nothing at all. This used to empty the store outright and count only the part that helped,
    /// which meant a button offering 7 GB back quietly made the other 19 GB cold as well.
    /// </para>
    /// <para>
    /// So what is left behind afterwards is exactly what the mod folders on this disk are running,
    /// and the bytes given back are exactly the figure the button offered.
    /// </para>
    /// <para>
    /// Safe to offer as a button because everything in a store is registered in some repo and
    /// therefore re-downloadable - the same property that lets eviction run without asking anybody.
    /// It costs downloads, never data.
    /// </para>
    /// <para>
    /// <b>The quarantine folder is not touched</b>, which is the whole distinction between the two:
    /// blobs are copies of something a server holds, and quarantined files are the ones sync found
    /// that <em>nothing</em> holds. See <see cref="ClearQuarantine"/>.
    /// </para>
    /// <para>
    /// A file that will not delete - one open in the game, say - is counted and skipped rather than
    /// throwing. This is housekeeping, and a store reclaimed except for the two files something is
    /// reading is the outcome that was wanted.
    /// </para>
    /// <para>
    /// On a copy-served disk nothing is hardlinked, so every entry is uniquely held and this does
    /// empty the store - correctly, because there every entry genuinely is costing its own bytes.
    /// </para>
    /// </remarks>
    public ContentStoreClearResult Reclaim(CancellationToken cancellationToken)
    {
        var deleted = 0;
        var failed = 0;
        long reclaimed = 0;

        foreach (var entry in Enumerate())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsUniquelyHeld is false)
            {
                continue;
            }

            try
            {
                File.Delete(entry.Path);

                deleted++;
                reclaimed += entry.Length;
            }
            catch (Exception exception)
            {
                Log.LogDebug(exception, "Could not delete the store blob {File}.", entry.Path);

                failed++;
            }
        }

        reclaimed += DeleteDirectory(Path.Combine(RootPath, _temporaryDirectory));

        return new ContentStoreClearResult(deleted, reclaimed, failed);
    }

    /// <summary>
    /// Deletes the files sync rescued here because they could not be recycled.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Clear"/> and asked about separately, because this is the one part of
    /// a store that is not re-downloadable: a quarantined file is a mod nothing in the repo
    /// registers, which is exactly why it was moved here instead of deleted.
    /// </remarks>
    /// <returns>The bytes reclaimed.</returns>
    public long ClearQuarantine()
    {
        return DeleteDirectory(Path.Combine(RootPath, _quarantineDirectory));
    }

    /// <summary>Where rescued files pile up, whether or not it exists yet.</summary>
    public string QuarantinePath => Path.Combine(RootPath, _quarantineDirectory);

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
            catch (Exception exception)
            {
                // Something else is reading it. It will be swept next time; everything in a store is
                // registered somewhere and therefore re-downloadable, so nothing here needs asking.
                Log.LogDebug(exception, "Could not evict the store blob {File}.", entry.Path);
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
    /// <param name="bytesRead">How much of the file has been read so far, for a hash that takes long enough to watch.</param>
    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken, IProgress<long>? bytesRead = null)
    {
        await using var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ModContentHasher.ComputeAsync(content, bytesRead, cancellationToken);
    }


    /// <summary>
    /// One entry of a full pass, tolerating a name that is not an address at all.
    /// </summary>
    /// <remarks>
    /// Only a walk of the directory can turn up such a name - every other route in is keyed by a
    /// hash the caller supplied. It is reported as unreadable and left alone: something put it there,
    /// this is not the code that knows what, and a verification pass is not the place to start
    /// deleting files it does not understand.
    /// </remarks>
    private async Task<BlobVerification> VerifyEntryAsync(ContentStoreEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            return await VerifyAsync(entry.Hash, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            Log.LogDebug(exception, "The store holds {File}, which is not a content address.", entry.Path);

            return BlobVerification.Unreadable;
        }
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
        catch (IOException exception) when (File.Exists(blobPath))
        {
            // Lost the race with another sync writing the same address.
            Log.LogDebug(exception, "Another writer got to the store blob {Hash} first.", hash);
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

    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            // A leftover temporary file costs disk space until the next sweep, nothing more.
            Log.LogDebug(exception, "Could not delete the temporary store file {File}.", path);
        }
    }

    /// <summary>The bytes under a directory, or zero where there is no such directory.</summary>
    private long MeasureDirectory(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(x => x.Length)
                : 0;
        }
        catch (Exception exception)
        {
            // A folder that cannot be walked is reported as empty rather than failing the page it
            // was measured for.
            Log.LogDebug(exception, "Could not measure {Directory}.", path);

            return 0;
        }
    }

    /// <returns>The bytes the directory held, whether or not every last file went.</returns>
    private long DeleteDirectory(string path)
    {
        var size = MeasureDirectory(path);

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            Log.LogDebug(exception, "Could not delete {Directory}.", path);
        }

        return size - MeasureDirectory(path);
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

/// <summary>Whether a blob still is what its address says it is.</summary>
public enum BlobVerification
{
    /// <summary>It hashes to its own name. Nothing has written through it.</summary>
    Intact,

    /// <summary>
    /// It does not. The bytes at a verified address are not the content that address names, which
    /// makes the entry worse than useless - every repo on the volume would be served it.
    /// </summary>
    Corrupt,

    /// <summary>The store does not hold that address, so there was nothing to check.</summary>
    Absent,

    /// <summary>
    /// It could not be read - open in the game, or a drive that went away mid-check. Unknown, and
    /// pointedly not <see cref="Corrupt"/>: the response to corrupt is deletion.
    /// </summary>
    Unreadable
}

/// <param name="RemainingBytes">What the store uniquely holds afterwards - the number the limit is about.</param>
public sealed record ContentStoreEvictionResult(int EntriesEvicted, long BytesReclaimed, long RemainingBytes);

/// <summary>
/// What one store is holding right now.
/// </summary>
/// <param name="TotalBytes">Every blob, whether or not deleting it would give the bytes back.</param>
/// <param name="ReclaimableBytes">
/// Only the entries the store uniquely holds - which is what the size limit counts, and the only
/// number emptying the store would actually free. Equal to <paramref name="TotalBytes"/> on a
/// copy-served disk, where nothing is hardlinked into a mod folder.
/// </param>
/// <param name="QuarantineBytes">
/// Files sync rescued because they could not be recycled. Not part of the store's size limit and not
/// re-downloadable, so they are reported apart from everything else.
/// </param>
public sealed record ContentStoreUsage(
    int Entries,
    long TotalBytes,
    long ReclaimableBytes,
    long TemporaryBytes,
    long QuarantineBytes)
{
    public static ContentStoreUsage Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <param name="Failed">Blobs something else was holding open. Swept the next time.</param>
public sealed record ContentStoreClearResult(int EntriesDeleted, long BytesReclaimed, int Failed);

/// <param name="Checked">Entries read to the end, so a cancelled pass says how far it got.</param>
/// <param name="Corrupt">
/// The addresses whose bytes no longer hash to them. Addresses and nothing else, because a store
/// does not know what a file <em>is</em> - naming the mods needs the manifests, which is
/// <see cref="ContentStoreMaintenance"/>'s half of the answer.
/// </param>
/// <param name="Removed">
/// How many of those actually went. Lower than <paramref name="Corrupt"/> where something has the
/// file open; the rest are caught by the next pass.
/// </param>
/// <param name="Unreadable">
/// Entries that could not be read, or that vanished mid-pass, or whose name is not a content address
/// at all. Not damage, and pointedly not counted as corrupt.
/// </param>
public sealed record ContentStoreVerificationResult(
    int Checked,
    long BytesRead,
    IReadOnlyList<string> Corrupt,
    int Removed,
    int Unreadable)
{
    public bool FoundProblems => Corrupt.Count > 0;
}

/// <summary>
/// How far a verification pass has got. Both counts and both byte totals, because file counts and
/// bytes disagree wildly across a store where one mod is 4 KB and the next is 400 MB.
/// </summary>
public sealed record ContentStoreVerificationProgress(int Checked, int Total, long BytesRead, long TotalBytes);


/// <summary>
/// Bytes that do not hash to the address they were offered under. Never stored, never installed.
/// </summary>
public sealed class ContentVerificationException(string expected, string actual)
    : Exception($"Content offered as '{expected}' hashes to '{actual}'. It was not stored.")
{
    public string ExpectedHash { get; } = expected;
    public string ActualHash { get; } = actual;
}
