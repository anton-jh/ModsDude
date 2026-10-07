using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Wpf.Shell.Navigation;

public interface IShellNavigationService
{
    void Register(MainPageViewModel shell);

    void Unregister(MainPageViewModel shell);

    /// <param name="driftedTarget">
    /// The folder that went out of step, so the editor can open with it already being scanned. It
    /// is the whole reason the user is being sent there - the versions the game downloaded are
    /// sitting in it, waiting to be imported.
    /// </param>
    /// <returns>False where the shell is not up yet, the target is gone, or navigation was refused.</returns>
    Task<bool> GoToProfileModsAsync(Guid repoId, Guid profileId, ModTargetRef driftedTarget);

    /// <summary>
    /// Into a repo's list of savegames. Reached from a prune that a savegame snapshot blocked, whose
    /// only useful next step is looking at that savegame.
    /// </summary>
    /// <returns>False where the shell is not up yet, the repo has no savegames, or navigation was refused.</returns>
    Task<bool> GoToSavegamesAsync(Guid repoId, Guid savegameId);

    /// <summary>
    /// Into a profile's own history, where any two revisions can be compared. Reached from a savegame,
    /// whose snapshots each name the revision they were played on - so "what changed under this save"
    /// is a question this already answers, and a cut-down comparison beside the savegame list would be
    /// a second answer to keep true.
    /// </summary>
    /// <returns>False where the shell is not up yet, the target is gone, or navigation was refused.</returns>
    /// <param name="selectRevision">
    /// Which revision to open at. A refused mod delete names the exact revisions holding it, and a
    /// link that landed on the head instead would make the user find the number themselves.
    /// </param>
    Task<bool> GoToProfileHistoryAsync(Guid repoId, Guid profileId, int? selectRevision = null);

    /// <summary>Builds the page on screen again, unless it holds unsaved changes. On the UI thread.</summary>
    void ReloadOpenPage();
}
