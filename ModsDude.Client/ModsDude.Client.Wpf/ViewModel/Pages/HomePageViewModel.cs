using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// Where the app opens: what the people this user shares repos with are playing, game by game.
/// </summary>
public sealed class HomePageViewModel : PageViewModel, IDisposable
{
    public HomePageViewModel(FriendActivityListViewModel.Factory friendsFactory)
    {
        Friends = friendsFactory.Create(onlyRepo: null);
    }


    public FriendActivityListViewModel Friends { get; }


    public void Dispose()
    {
        Friends.Dispose();
    }


    /// <summary>
    /// Read again on every visit - the watcher only asks every few minutes, and somebody opening Home
    /// is asking now. Failing is said on the page rather than in a dialog: see the list.
    /// </summary>
    protected override Task InitAsync() => Friends.RefreshAsync();
}
