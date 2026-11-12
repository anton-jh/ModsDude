using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <param name="Hash">The address the bytes no longer hash to.</param>
/// <param name="FileName">The mod folder file that turned out to be the blob itself.</param>
/// <param name="VolumeRoot">
/// Which volume's store held it. The blast radius is the volume, not the game the check happened
/// to run for, so the notice has to be able to say so.
/// </param>
/// <param name="Removed">
/// Whether the bad entry is gone. False where the delete failed - something has it open - in which
/// case it is found again on the next check.
/// </param>
public sealed record CorruptedBlob(
    ModKey ModId,
    string DisplayName,
    string Hash,
    string FileName,
    string VolumeRoot,
    bool Removed);

/// <summary>
/// Catches a store blob that was rewritten in place through a hardlink.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection rather than prevention.</b> The alternative was making blobs read-only, which on
/// Windows also blocks unlinking a name and therefore breaks the very rename-over an in-game updater
/// relies on. Every blob is registered in some repo and re-downloadable, so a corrupt entry found
/// promptly costs a download.
/// </para>
/// <para>
/// The whole trick is <b>what it does not read</b>. Only a file whose size or time moved since the
/// apply can have been written, a rename-over leaves the mod folder holding a <em>new</em> file, and
/// comparing two file identities costs two handle opens. Only an entry that fails that comparison -
/// normally none of them - is ever read, and one read as intact is not read again until it moves
/// again. See <see cref="FileLinks.TryGetFileIdentity"/>.
/// </para>
/// </remarks>
public sealed class StoreIntegrityService(
    IContentStoreProvider storeProvider,
    ISyncManifestStore manifestStore,
    IModFolders modFolders,
    ILogger<StoreIntegrityService> logger) : IStoreIntegrityService
{
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly List<CorruptedBlob> _found = [];
    private readonly HashSet<(string Hash, long Size, DateTime ModifiedUtc)> _verifiedIntact = [];


    public event EventHandler? Changed;


    public IReadOnlyList<CorruptedBlob> Found
    {
        get
        {
            lock (_lock)
            {
                return [.. _found];
            }
        }
    }


    public async Task<IReadOnlyList<CorruptedBlob>> CheckAsync(ModTargetRef target, CancellationToken cancellationToken)
    {
        // One check at a time, so two that reach the same blob do not both verify it and report it.
        await _checking.WaitAsync(cancellationToken);

        try
        {
            return await CheckCoreAsync(target, cancellationToken);
        }
        finally
        {
            _checking.Release();
        }
    }

    public void MarkSuspect(ContentStore store, IReadOnlySet<string> hashes)
    {
        if (hashes.Count == 0)
        {
            return;
        }

        foreach (var folder in modFolders.GetAll())
        {
            try
            {
                if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(folder.ModFolder).RootPath, store.RootPath))
                {
                    manifestStore.MarkSuspect(folder.Target, hashes);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Its next apply keeps the file rather than replacing it, and drift does not name it.
                logger.LogWarning(exception, "Could not mark the corrupt blobs suspect in the manifest of target {Target}.", folder.Target);
            }
        }
    }


    private async Task<IReadOnlyList<CorruptedBlob>> CheckCoreAsync(ModTargetRef target, CancellationToken cancellationToken)
    {
        if (manifestStore.TryRead(target) is not SyncManifest manifest)
        {
            return [];
        }

        var store = storeProvider.GetStoreServing(manifest.ModFolder);
        var found = new List<CorruptedBlob>();

        foreach (var entry in manifest.Entries.Where(x => x.ContentSuspect is false).OrderBy(x => x.FileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installed = new FileInfo(Path.Combine(manifest.ModFolder, entry.FileName));

            if (installed.Exists is false ||
                (installed.Length == entry.Size && installed.LastWriteTimeUtc == entry.ModifiedUtc) ||
                WasWrittenInPlace(store, installed.FullName, entry.ContentHash) is false)
            {
                continue;
            }

            var seen = (entry.ContentHash.ToLowerInvariant(), installed.Length, installed.LastWriteTimeUtc);

            lock (_lock)
            {
                if (_verifiedIntact.Contains(seen))
                {
                    continue;
                }
            }

            // Only now is anything read. Reaching here means the file the game changed and the blob
            // every repo on this volume shares are one file, so the store is holding bytes that are
            // not what its address says - unless only the timestamp moved, which is what the hash
            // is here to establish before anything gets deleted.
            var verification = await store.VerifyAsync(entry.ContentHash, cancellationToken);

            if (verification is BlobVerification.Intact)
            {
                lock (_lock)
                {
                    _verifiedIntact.Add(seen);
                }

                continue;
            }

            if (verification is not BlobVerification.Corrupt)
            {
                continue;
            }

            logger.LogWarning(
                "The store blob {Hash} on {Volume} was rewritten in place through {File} and no longer matches its address. Removing it.",
                entry.ContentHash,
                store.VolumeRoot,
                entry.FileName);

            found.Add(new CorruptedBlob(
                ModKey.From(entry.ModId),
                entry.DisplayName ?? entry.ModId,
                entry.ContentHash,
                entry.FileName,
                store.VolumeRoot,
                store.Remove(entry.ContentHash)));
        }

        if (found.Count == 0)
        {
            return [];
        }

        MarkSuspect(store, new HashSet<string>(found.Select(x => x.Hash), StringComparer.OrdinalIgnoreCase));
        Record(found);

        return found;
    }

    private void Record(IReadOnlyList<CorruptedBlob> found)
    {
        bool grew;

        lock (_lock)
        {
            var before = _found.Count;

            foreach (var blob in found.Where(blob => _found.Any(x => ModContentHasher.Matches(x.Hash, blob.Hash)) is false))
            {
                _found.Add(blob);
            }

            grew = _found.Count > before;
        }

        if (grew)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether the file in the mod folder is still the same file as the store blob, which is the
    /// only way the store can have been written to.
    /// </summary>
    /// <remarks>
    /// Both halves of the answer are read at the same moment on purpose. A file index means nothing
    /// across a deletion, so this is a question about right now and never one to cache.
    /// </remarks>
    private static bool WasWrittenInPlace(ContentStore store, string installedPath, string hash)
        => store.Contains(hash)
            && FileLinks.TryGetFileIdentity(installedPath) is FileIdentity installed
            && installed == FileLinks.TryGetFileIdentity(store.GetBlobPath(hash));
}
