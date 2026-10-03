using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Repos;

namespace ModsDude.Client.Wpf.Shell.Navigation;

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
public sealed class ShellNavigationService : IShellNavigationService
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

    public async Task<bool> GoToProfileModsAsync(Guid repoId, Guid profileId, ModTargetRef driftedTarget)
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

        return profilePage.TrySelectMods(driftedTarget);
    }

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
