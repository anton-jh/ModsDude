using ModsDude.Client.Core.Disks;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// What executing sync plans writes, per volume.
/// </summary>
/// <remarks>
/// Removals free nothing: a file sent to the Recycle Bin stays on its volume. Staged files need no
/// room of their own, because each is renamed into the place it was counted for.
/// </remarks>
internal sealed class ModSyncSpaceNeeds
{
    private readonly DiskSpaceNeeds _needs = new();

    /// <summary>Per store root, the addresses already counted: a store fills each one once, however many plans want it.</summary>
    private readonly Dictionary<string, HashSet<string>> _stored = new(StringComparer.OrdinalIgnoreCase);


    private ModSyncSpaceNeeds()
    {
    }


    public static DiskSpaceNeeds For(IEnumerable<ModSyncPlan> plans)
    {
        var builder = new ModSyncSpaceNeeds();

        foreach (var plan in plans)
        {
            builder.Add(plan);
        }

        return builder._needs;
    }


    private void Add(ModSyncPlan plan)
    {
        foreach (var hash in plan.HashesToFetch.Where(x => plan.ServingStore.Contains(x) is false))
        {
            AddToStore(plan.ServingStore, hash, SizeOf(plan, hash));
        }

        foreach (var item in plan.Items)
        {
            AddMaterialization(plan, item);
            AddRemoval(plan, item);
        }
    }

    private void AddMaterialization(ModSyncPlan plan, ModSyncItem item)
    {
        if (item.Action is ModSyncAction.Install or ModSyncAction.Replace &&
            plan.Materialization.Method is MaterializationMethod.Copy)
        {
            _needs.Add(plan.ModFolder, item.DesiredSize.GetValueOrDefault());
        }

        if (item.Action is ModSyncAction.Unlink)
        {
            _needs.Add(plan.ModFolder, item.InstalledSize);
        }
    }

    /// <summary>
    /// The removal phase's two routes: kept in the serving store where no store has the bytes, or
    /// moved aside. Either costs room only where it leaves the mod folder's volume.
    /// </summary>
    private void AddRemoval(ModSyncPlan plan, ModSyncItem item)
    {
        if (item.InstalledPath is null ||
            item.Action is not (ModSyncAction.Replace or ModSyncAction.UninstallRecoverable or ModSyncAction.Quarantine))
        {
            return;
        }

        if (item.InstalledIsRecoverable && item.InstalledHash is string hash)
        {
            if (plan.AllStores.Any(x => x.Contains(hash)) is false && IsOtherVolume(plan, plan.ServingStore.RootPath))
            {
                AddToStore(plan.ServingStore, hash, item.InstalledSize);
            }
        }
        else if (plan.QuarantineFolder is string chosen && IsOtherVolume(plan, chosen))
        {
            _needs.Add(chosen, item.InstalledSize);
        }
    }

    private void AddToStore(ContentStore store, string hash, long bytes)
    {
        if (_stored.TryGetValue(store.RootPath, out var hashes) is false)
        {
            _stored[store.RootPath] = hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (hashes.Add(hash))
        {
            _needs.Add(store.RootPath, bytes);
        }
    }

    private static long SizeOf(ModSyncPlan plan, string hash)
        => plan.Items
            .Where(x => string.Equals(x.DesiredHash, hash, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.DesiredSize.GetValueOrDefault())
            .FirstOrDefault();

    private static bool IsOtherVolume(ModSyncPlan plan, string path)
        => string.Equals(
            FileSystemHelper.NormalizeVolumeRoot(plan.ModFolder),
            FileSystemHelper.NormalizeVolumeRoot(path),
            StringComparison.OrdinalIgnoreCase) is false;
}
