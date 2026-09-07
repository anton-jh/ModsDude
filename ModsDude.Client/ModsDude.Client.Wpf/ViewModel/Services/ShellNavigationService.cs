using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// Deep-links into the sidebar's nested navigation from outside it - which today means the app-level
/// drift notice, whose whole point is being reachable from any view.
/// </summary>
/// <remarks>
/// The shell registers itself rather than being handed in, because it is built by the login
/// transition and replaced whenever that runs again. Every step is allowed to fail quietly: a page
/// holding unsaved changes refuses navigation, and being refused is a legitimate answer rather than
/// something to force past.
/// </remarks>
public sealed class ShellNavigationService
{
    private MainPageViewModel? _shell;


    public void Register(MainPageViewModel shell) => _shell = shell;

    public void Unregister(MainPageViewModel shell)
    {
        if (ReferenceEquals(_shell, shell))
        {
            _shell = null;
        }
    }

    /// <param name="driftedInstanceId">
    /// The instance whose mod folder went out of step, so the editor can open with that folder
    /// already being scanned. It is the whole reason the user is being sent there - the versions the
    /// game downloaded are sitting in it, waiting to be imported.
    /// </param>
    /// <returns>False where the shell is not up yet, the target is gone, or navigation was refused.</returns>
    public async Task<bool> GoToProfileModsAsync(Guid repoId, Guid profileId, Guid driftedInstanceId)
    {
        if (_shell is not MainPageViewModel shell)
        {
            return false;
        }

        if (await shell.TrySelectRepoAsync(repoId) is not RepoPageViewModel repoPage)
        {
            return false;
        }

        if (await repoPage.TrySelectProfileAsync(profileId) is not ProfilePageViewModel profilePage)
        {
            return false;
        }

        return profilePage.TrySelectMods(driftedInstanceId);
    }

    /// <summary>
    /// Into a repo's list of savegames. Reached from a prune that a savegame version blocked, whose
    /// only useful next step is looking at that savegame.
    /// </summary>
    /// <returns>False where the shell is not up yet, the repo has no savegames, or navigation was refused.</returns>
    public async Task<bool> GoToSavegamesAsync(Guid repoId, Guid savegameId)
    {
        if (_shell is not MainPageViewModel shell)
        {
            return false;
        }

        if (await shell.TrySelectRepoAsync(repoId) is not RepoPageViewModel repoPage)
        {
            return false;
        }

        // The saves list for a live savegame, the Archive with the row picked out for an archived
        // one - an archived savegame has no row on the saves list, and the archive row is the
        // savegame.
        return await repoPage.TrySelectSavegameAsync(savegameId);
    }

    /// <summary>
    /// Into a profile's own history, where any two revisions can be compared. Reached from a savegame,
    /// whose versions each name the revision they were played on - so "what changed under this save"
    /// is a question this already answers, and a cut-down comparison beside the savegame list would be
    /// a second answer to keep true.
    /// </summary>
    /// <returns>False where the shell is not up yet, the target is gone, or navigation was refused.</returns>
    /// <param name="selectRevision">
    /// Which revision to open at. A refused mod delete names the exact revisions holding it, and a
    /// link that landed on the head instead would make the user find the number themselves.
    /// </param>
    public async Task<bool> GoToProfileHistoryAsync(Guid repoId, Guid profileId, int? selectRevision = null)
    {
        if (_shell is not MainPageViewModel shell)
        {
            return false;
        }

        if (await shell.TrySelectRepoAsync(repoId) is not RepoPageViewModel repoPage)
        {
            return false;
        }

        if (await repoPage.TrySelectProfileAsync(profileId) is not ProfilePageViewModel profilePage)
        {
            return false;
        }

        return profilePage.TrySelectHistory(selectRevision);
    }
}
