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

    /// <returns>False where the shell is not up yet, the target is gone, or navigation was refused.</returns>
    public async Task<bool> GoToProfileModsAsync(Guid repoId, Guid profileId)
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

        return profilePage.TrySelectMods();
    }
}
