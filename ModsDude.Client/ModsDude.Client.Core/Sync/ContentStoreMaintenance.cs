using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// The store housekeeping a settings page offers: what is on this machine, how much of it there is,
/// and the two ways to make it smaller.
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
    IInstanceModFolders instanceModFolders,
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
    /// whose disk no longer holds any instance is exactly the one worth being able to empty, so it
    /// cannot be dropped for having nothing to serve.
    /// </remarks>
    public IReadOnlyList<ContentStore> GetStores()
    {
        var serving = instanceModFolders.GetAll()
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
    /// here. What each served instance is running comes off its sync manifest, so this asks the
    /// network nothing and works offline.
    /// </remarks>
    public ContentStoreEvictionResult Sweep(ContentStore store, CancellationToken cancellationToken)
    {
        return store.Evict(GetPinnedHashes(store), cancellationToken);
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

        foreach (var instance in instanceModFolders.GetAll())
        {
            try
            {
                if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(instance.ModFolder).RootPath, store.RootPath) is false)
                {
                    continue;
                }

                foreach (var entry in manifestStore.TryRead(instance.InstanceId)?.Entries ?? [])
                {
                    pinned.Add(entry.ContentHash);
                }
            }
            catch (Exception exception)
            {
                // An instance whose folder cannot be resolved to a store contributes no pins, which
                // costs a possible re-download rather than a failed sweep.
                logger.LogDebug(exception, "Could not read what instance {Instance} is running.", instance.InstanceId);
            }
        }

        return pinned;
    }
}
