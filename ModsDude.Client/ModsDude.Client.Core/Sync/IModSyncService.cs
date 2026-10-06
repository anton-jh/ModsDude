namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Makes a game's mod folder contain exactly what a profile pins: plan first, show it, then execute.
/// </summary>
public interface IModSyncService
{
    /// <summary>
    /// Raised with the folder's path when an apply has changed what is in a mod folder, after the
    /// files moved and the lease on the stores was released. Raised from whichever thread ran the
    /// apply, so a handler that touches the UI marshals.
    /// </summary>
    event Action<string>? ModFolderChanged;

    /// <param name="progress">
    /// Where to report which mod is being examined. Worth passing: on a folder whose files no longer
    /// match the manifest this reads and hashes every one of them.
    /// </param>
    /// <exception cref="GameProcesses.GameRunningException">The game is running.</exception>
    Task<ModSyncPlan> PlanAsync(ModSyncRequest request, CancellationToken cancellationToken, IProgress<ModSyncProgress>? progress = null);

    /// <exception cref="GameProcesses.GameRunningException">The game is running.</exception>
    Task<ModSyncResult> ExecuteAsync(ModSyncPlan plan, IProgress<ModSyncProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Puts what a profile pins into the stores serving these folders, touching no folder. For an
    /// activation that has to wait for something else first, so a download that fails stops it before
    /// anything has changed. The activation still plans and fetches as ever, and finds them there.
    /// </summary>
    /// <returns>Completed where every store holds every mod; the failures otherwise.</returns>
    Task<ModSyncResult> FetchAsync(ModFetchRequest request, IProgress<ModSyncProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Records a folder that already matches its profile, without touching a file in it - so a mod
    /// added by hand and then pinned stops being reported as drift. Only for a plan with no work.
    /// </summary>
    Task RecordAlreadyMatchedAsync(ModSyncPlan plan);
}
