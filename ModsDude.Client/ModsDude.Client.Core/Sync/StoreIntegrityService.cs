using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
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
/// Catches a store blob that was rewritten in place through a hardlink, at the moment drift says
/// a mod folder file changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection rather than prevention</b>, and the reasoning is in
/// docs/07-mod-sync-design.md#detecting-a-rewritten-blob. The alternative was making blobs
/// read-only, which on Windows also blocks unlinking a name and therefore breaks the very
/// rename-over an in-game updater relies on. Every blob is registered in some repo and
/// re-downloadable, so a corrupt entry found promptly costs a download - which is close enough to
/// what preventing it would have bought to not be worth breaking the updater for.
/// </para>
/// <para>
/// The whole trick is <b>what it does not read</b>. An in-game update-all changes hundreds of files
/// and hashing them would be tens of gigabytes; but a rename-over leaves the mod folder holding a
/// <em>new</em> file, so the blob and the installed file stop being the same file. Comparing two
/// file identities costs two handle opens, and only an entry that fails that comparison - normally
/// none of them - is ever read. See <see cref="FileLinks.TryGetFileIdentity"/>.
/// </para>
/// <para>
/// Computed apart from <see cref="DriftService"/> and passed to it, for the reason
/// <see cref="DriftReport.SavegameDrift"/> gives: that class is a synchronous comparison of a
/// manifest against a directory listing, and this one needs the store provider and reads file bytes.
/// </para>
/// </remarks>
public sealed class StoreIntegrityService(
    IContentStoreProvider storeProvider,
    SyncManifestStore manifestStore,
    ILogger<StoreIntegrityService> logger)
{
    /// <summary>
    /// Checks the changed files of one game, and drops any blob that proves to have been
    /// rewritten.
    /// </summary>
    /// <param name="changed">
    /// The names <see cref="DriftService"/> found no longer matching the manifest. Nothing
    /// else can have been rewritten: a file whose size and time still match what was installed is
    /// one nothing has written to.
    /// </param>
    /// <remarks>
    /// Copy-served games fall out for free rather than by a special case. Nothing there is
    /// hardlinked, so the installed file is never the same file as the blob and every candidate is
    /// discarded by the identity comparison - which is also what happens on a platform or filesystem
    /// that cannot answer the question at all.
    /// </remarks>
    public async Task<IReadOnlyList<CorruptedBlob>> CheckAsync(
        GameIdentity game,
        string modFolder,
        IReadOnlyList<string> changed,
        CancellationToken cancellationToken)
    {
        if (changed.Count == 0)
        {
            return [];
        }

        var manifest = manifestStore.TryRead(game);

        if (manifest is null)
        {
            return [];
        }

        var store = storeProvider.GetStoreServing(modFolder);
        var byName = manifest.Entries.ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);
        var found = new List<CorruptedBlob>();

        foreach (var name in changed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (byName.TryGetValue(name, out var entry) is false)
            {
                continue;
            }

            if (WasWrittenInPlace(store, modFolder, name, entry.ContentHash) is false)
            {
                continue;
            }

            // Only now is anything read. Reaching here means the file the game changed and the blob
            // every repo on this volume shares are one file, so the store is holding bytes that are
            // not what its address says - unless only the timestamp moved, which is what the hash
            // is here to establish before anything gets deleted.
            var verification = await store.VerifyAsync(entry.ContentHash, cancellationToken);

            if (verification is not BlobVerification.Corrupt)
            {
                continue;
            }

            logger.LogWarning(
                "The store blob {Hash} on {Volume} was rewritten in place through {File} and no longer matches its address. Removing it.",
                entry.ContentHash,
                store.VolumeRoot,
                name);

            var removed = store.Remove(entry.ContentHash);

            found.Add(new CorruptedBlob(
                ModKey.From(entry.ModId),
                entry.DisplayName ?? entry.ModId,
                entry.ContentHash,
                name,
                store.VolumeRoot,
                removed));
        }

        return found;
    }


    /// <summary>
    /// Whether the file in the mod folder is still the same file as the store blob, which is the
    /// only way the store can have been written to.
    /// </summary>
    /// <remarks>
    /// Both halves of the answer are read at the same moment on purpose. A file index means nothing
    /// across a deletion, so this is a question about right now and never one to cache.
    /// </remarks>
    private static bool WasWrittenInPlace(ContentStore store, string modFolder, string fileName, string hash)
    {
        if (store.Contains(hash) is false)
        {
            return false;
        }

        var installed = FileLinks.TryGetFileIdentity(Path.Combine(modFolder, fileName));

        if (installed is null)
        {
            return false;
        }

        return installed == FileLinks.TryGetFileIdentity(store.GetBlobPath(hash));
    }
}
