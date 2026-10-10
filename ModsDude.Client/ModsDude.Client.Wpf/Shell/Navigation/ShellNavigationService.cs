using ModsDude.Client.Wpf.Repos;

namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>
/// Deep-links into the sidebar's nested navigation from outside it: the notices, which are reachable
/// from any view, and pages such as Home that lead into a repo.
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

    public void ReloadOpenPage() => _shell?.NavManager.ReloadInnermost();

    public void Unregister(MainPageViewModel shell)
    {
        if (ReferenceEquals(_shell, shell))
        {
            _shell = null;
        }
    }

    public async Task<bool> GoToAsync(Guid repoId, RepoDestination destination)
    {
        if (_shell is not MainPageViewModel shell)
        {
            return false;
        }

        if (await shell.TrySelectRepoAsync(repoId) is not RepoPageViewModel repoPage)
        {
            return false;
        }

        return await repoPage.TrySelectAsync(destination);
    }

    public void GoToJoinOrCreate() => _shell?.SelectJoinOrCreate();
}
