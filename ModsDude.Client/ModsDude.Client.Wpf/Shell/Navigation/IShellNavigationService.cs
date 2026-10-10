namespace ModsDude.Client.Wpf.Shell.Navigation;

public interface IShellNavigationService
{
    void Register(MainPageViewModel shell);

    void Unregister(MainPageViewModel shell);

    /// <returns>
    /// False where the shell is not up yet, the repo or the destination is gone or closed to this
    /// membership level, or navigation was refused.
    /// </returns>
    Task<bool> GoToAsync(Guid repoId, RepoDestination destination);

    void GoToJoinOrCreate();

    /// <summary>Builds the page on screen again, unless it holds unsaved changes. On the UI thread.</summary>
    void ReloadOpenPage();
}
