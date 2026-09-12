using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

/// <param name="ModFolder">The folder still holding the bad bytes, whatever the store now says.</param>
/// <param name="ProfileName">What the manifest recorded was applied there, where it recorded one.</param>
/// <param name="Mods">How many of the failed addresses that folder is running.</param>
public sealed record AffectedModFolder(string ModFolder, string? ProfileName, int Mods);

/// <param name="Affected">
/// The mod folders that need re-applying. Empty is the good answer and the usual one - a corrupt
/// blob nothing was running is repaired completely by dropping it.
/// </param>
public sealed record StoreVerificationReport(
    ContentStoreVerificationResult Result,
    IReadOnlyList<AffectedModFolder> Affected);

/// <summary>
/// The store housekeeping a settings page offers: what is on this machine, how much of it there is,
/// whether it is still what it claims to be, and the two ways to make it smaller.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is deliberately available to the user rather than only to a sync. A store fills a
/// disk quietly - it is capped, but the cap is a number somebody accepted once and may since have
/// regretted - and a mod folder that has been repointed leaves a store behind that nothing sweeps,
/// because eviction only ever runs on the store a sync is using.
/// See docs/07-mod-sync-design.md#store-eviction-and-the-size-limit.
/// </para>
/// <para>
/// Nothing here can lose data: every blob is registered in some repo and re-downloadable, which is
/// the same property eviction leans on. The one exception is the quarantine folder, which is why
/// <see cref="ContentStore.ClearQuarantine"/> is a separate act with its own question.
/// </para>
/// </remarks>
public sealed class ContentStoreMaintenance(
    IContentStoreProvider storeProvider,
    IModFolders modFolders,
    SyncManifestStore manifestStore,
    ILogger<ContentStoreMaintenance> logger)
{
    /// <summary>
    /// Every store on this machine: the ones serving a mod folder now, and the ones settings still
    /// name.
    /// </summary>
    /// <remarks>
    /// Both halves are needed and neither is a superset of the other.
    /// <see cref="IContentStoreProvider.GetAllStores"/> reads settings, so it misses the store a
    /// mod folder is served by under the defaults nobody has visited a page to accept; and a store
    /// whose disk no longer holds any game is exactly the one worth being able to empty, so it
    /// cannot be dropped for having nothing to serve.
    /// </remarks>
    public IReadOnlyList<ContentStore> GetStores()
    {
        var serving = modFolders.GetAll()
            .Select(x => x.ModFolder)
            .Select(storeProvider.GetStoreServing);

        return [.. serving
            .Concat(storeProvider.GetAllStores())
            .DistinctBy(x => FileSystemHelper.NormalizePathForComparison(x.RootPath))
            .OrderBy(x => x.VolumeRoot, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Trims a store back inside its size limit, sparing what the mod folders it serves are running.
    /// </summary>
    /// <remarks>
    /// The same sweep a sync ends with, minus the profile being applied - which there is not one of
    /// here. What each served game is running comes off its sync manifest, so this asks the
    /// network nothing and works offline.
    /// </remarks>
    public ContentStoreEvictionResult Sweep(ContentStore store, CancellationToken cancellationToken)
    {
        return store.Evict(GetPinnedHashes(store), cancellationToken);
    }

    /// <summary>
    /// Reads every blob in a store, drops the ones that no longer match their address, and says which
    /// mod folders are still running them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half a store cannot answer for itself. <see cref="ContentStore.VerifyAllAsync"/> reports
    /// addresses, because a store deliberately knows nothing about what a file <em>is</em>; the
    /// manifests here turn those into folders and profile names. That matters because <b>removing the
    /// blob is not the repair</b>. Where the entry was hardlinked into a mod folder, that folder is
    /// still holding the same wrong bytes under the same name, and only re-applying its profile
    /// replaces them - so a pass that found something has to be able to say where to go next.
    /// </para>
    /// <para>
    /// Answered from the manifests, so it asks the network nothing and works offline - the same
    /// bargain <see cref="Sweep"/> makes.
    /// </para>
    /// </remarks>
    public async Task<StoreVerificationReport> VerifyAsync(
        ContentStore store,
        IProgress<ContentStoreVerificationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = await store.VerifyAllAsync(progress, cancellationToken);

        return new StoreVerificationReport(result, FindAffectedFolders(store, result.Corrupt));
    }

    /// <summary>
    /// Which mod folders this store serves are running one of the bad addresses.
    /// </summary>
    /// <remarks>
    /// Every served game is asked, not only the drifted ones. A folder whose file still matches
    /// its manifest exactly is the <em>worst</em> case here, not the safe one: it means nothing has
    /// noticed, and under hardlinking that file is the blob that just failed.
    /// </remarks>
    private List<AffectedModFolder> FindAffectedFolders(ContentStore store, IReadOnlyList<string> corrupt)
    {
        if (corrupt.Count == 0)
        {
            return [];
        }

        var bad = new HashSet<string>(corrupt, StringComparer.OrdinalIgnoreCase);
        var affected = new List<AffectedModFolder>();

        foreach (var folder in modFolders.GetAll())
        {
            try
            {
                if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(folder.ModFolder).RootPath, store.RootPath) is false)
                {
                    continue;
                }

                var manifest = manifestStore.TryRead(folder.Target);
                var hits = manifest?.Entries.Count(x => bad.Contains(x.ContentHash)) ?? 0;

                if (hits > 0)
                {
                    affected.Add(new AffectedModFolder(folder.ModFolder, manifest?.ProfileName, hits));
                }
            }
            catch (Exception exception)
            {
                // One folder that cannot be resolved costs its name in the report, not the report.
                logger.LogDebug(exception, "Could not tell whether target {Target} is running a bad blob.", folder.Target);
            }
        }

        return affected;
    }

    /// <summary>
    /// What the mod folders this store serves are currently running, and therefore what a sweep must
    /// leave alone.
    /// </summary>
    /// <remarks>
    /// Dropping one of these would not break anything - the file in the mod folder holds its own
    /// bytes on a copy-served disk and survives losing a name on a hardlinked one - but it would
    /// guarantee a re-download the next time that profile is applied, which is the opposite of what
    /// somebody clicking "sweep" is asking for.
    /// </remarks>
    private IReadOnlySet<string> GetPinnedHashes(ContentStore store)
    {
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in modFolders.GetAll())
        {
            try
            {
                if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(folder.ModFolder).RootPath, store.RootPath) is false)
                {
                    continue;
                }

                foreach (var entry in manifestStore.TryRead(folder.Target)?.Entries ?? [])
                {
                    pinned.Add(entry.ContentHash);
                }
            }
            catch (Exception exception)
            {
                // A folder that cannot be resolved to a store contributes no pins, which
                // costs a possible re-download rather than a failed sweep.
                logger.LogDebug(exception, "Could not read what target {Target} is running.", folder.Target);
            }
        }

        return pinned;
    }
}
