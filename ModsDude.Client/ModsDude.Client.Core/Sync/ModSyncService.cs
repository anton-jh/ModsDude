using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Sync;

/// <remarks>
/// <para>
/// The execution order is the safety property. The serving store is filled with everything
/// <b>this profile</b> needs before anything in the mod folder is touched, so a failure or a
/// cancellation during the slow part leaves the game exactly as it was, and the destructive phase
/// only ever runs against a store that already holds what the profile needs. There is no prefetching
/// of the repo's full mod set - at thousands of registered versions that is tens of gigabytes for
/// content the user may never activate.
/// </para>
/// <para>
/// Everything reports per mod. Two thousand files is minutes of work even on the fast path, and a
/// frozen progress bar is indistinguishable from a hang.
/// </para>
/// </remarks>
public sealed class ModSyncService(
    IModDependenciesClient modDependenciesClient,
    IModStore modStore,
    IFilesClient filesClient,
    IModFileDownloader downloader,
    IContentStoreProvider storeProvider,
    ISyncManifestStore manifestStore,
    IRecycleBin recycleBin,
    IModFolders modFolders,
    IHeldSavegames heldSavegames,
    ISavegamePlayAttribution playAttribution,
    IResourceLeases leases,
    IGameFileEditor fileEditor,
    IGameRunningGuard runningGuard,
    TimeProvider timeProvider,
    ILogger<ModSyncService> logger)
    : IModSyncService
{
    private readonly ModSyncPlanBuilder _planBuilder = new(
        modDependenciesClient, modStore, storeProvider, manifestStore, heldSavegames, fileEditor, runningGuard, logger);

    private readonly ModFetcher _fetcher = new(filesClient, downloader, logger);

    private readonly ModSyncExecutor _executor = new(
        new ModFetcher(filesClient, downloader, logger), storeProvider, manifestStore, recycleBin, modFolders, playAttribution, leases,
        fileEditor, runningGuard, timeProvider, logger);


    public event Action<string>? ModFolderChanged
    {
        add => _executor.ModFolderChanged += value;
        remove => _executor.ModFolderChanged -= value;
    }


    public Task<ModSyncPlan> PlanAsync(ModSyncRequest request, CancellationToken cancellationToken, IProgress<ModSyncProgress>? progress = null)
        => _planBuilder.PlanAsync(request, cancellationToken, progress);

    public Task<ModSyncResult> ExecuteAsync(ModSyncPlan plan, IProgress<ModSyncProgress>? progress, CancellationToken cancellationToken)
        => _executor.ExecuteAsync(plan, progress, cancellationToken);

    public async Task<ModSyncResult> FetchAsync(ModFetchRequest request, IProgress<ModSyncProgress>? progress, CancellationToken cancellationToken)
    {
        var (desired, _) = await ModSyncPlanBuilder.GetDesiredAsync(
            modDependenciesClient, request.RepoId, request.ProfileId, request.Revision, cancellationToken);

        var servingStores = request.ModFolders
            .Select(storeProvider.GetStoreServing)
            .DistinctBy(x => x.RootPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allStores = storeProvider.GetAllStores()
            .Concat(servingStores)
            .DistinctBy(x => x.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var store in servingStores)
        {
            store.EnsureUsable();
        }

        using var lease = await leases.AcquireSharedAsync(
            allStores.Select(ResourceKeys.Store), "Downloading a profile's mods", cancellationToken);

        foreach (var store in servingStores)
        {
            var failures = await _fetcher.FetchAsync(
                request.RepoId,
                store,
                allStores,
                desired.Select(x => new ModFetch(x.ModId, x.VersionId, x.ContentHash, x.DisplayName ?? x.ModId.Value)),
                progress,
                cancellationToken);

            // The next store would fail on the same mods.
            if (failures.Count > 0)
            {
                return new ModSyncResult(false, failures);
            }
        }

        return new ModSyncResult(true, []);
    }

    public async Task RecordAlreadyMatchedAsync(ModSyncPlan plan)
    {
        if (plan.HasWork)
        {
            throw new InvalidOperationException(
                "A plan with work in it has to be executed; executing it is what records the result.");
        }

        await _executor.WriteManifestAsync(plan);
    }
}
