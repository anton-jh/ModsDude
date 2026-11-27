using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.Specialized;

namespace ModsDude.Client.Wpf.Home;

/// <summary>
/// Where the app opens: every game on this machine and every repo, with the profiles friends are on
/// in it and the button that joins them.
/// </summary>
public sealed partial class HomePageViewModel : PageViewModel, IDisposable
{
    private readonly IRepoStore _repos;
    private readonly IShellNavigationService _navigation;


    public HomePageViewModel(
        FriendActivityListViewModel.Factory friendsFactory,
        HomeRepoListViewModel.Factory repoListFactory,
        IRepoStore repos,
        IShellNavigationService navigation)
    {
        _repos = repos;
        _navigation = navigation;

        Friends = friendsFactory.CreateForAllRepos();
        RepoList = repoListFactory.Create(Friends);

        _repos.Repos.CollectionChanged += OnReposChanged;
        RepoList.Groups.CollectionChanged += OnGroupsChanged;
    }


    /// <summary>What friends are on in every repo, which the repo list splits by repo.</summary>
    public FriendActivityListViewModel Friends { get; }

    public HomeRepoListViewModel RepoList { get; }

    public bool HasRepos => _repos.Repos.Count > 0;

    public bool HasNoRepos => HasRepos is false;

    /// <summary>Whether there is a game to list: one a repo is about, or one connected here.</summary>
    public bool HasGames => RepoList.Groups.Count > 0;


    public void Dispose()
    {
        _repos.Repos.CollectionChanged -= OnReposChanged;
        RepoList.Groups.CollectionChanged -= OnGroupsChanged;

        RepoList.Dispose();
        Friends.Dispose();
    }


    protected override async Task InitAsync()
    {
        await Friends.RefreshAsync();
        await RepoList.InitAsync();
    }


    [RelayCommand]
    private void JoinOrCreate() => _navigation.GoToJoinOrCreate();


    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRepos));
        OnPropertyChanged(nameof(HasNoRepos));
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasGames));
    }
}
