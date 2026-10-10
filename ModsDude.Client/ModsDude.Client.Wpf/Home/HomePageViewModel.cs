using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.Specialized;

namespace ModsDude.Client.Wpf.Home;

/// <summary>Where the app opens: what friends are playing, with the button that joins them, over every repo.</summary>
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
    }


    /// <summary>Every friend's game in every repo, whoever is playing first.</summary>
    public FriendActivityListViewModel Friends { get; }

    public HomeRepoListViewModel RepoList { get; }

    public bool HasRepos => _repos.Repos.Count > 0;

    public bool HasNoRepos => HasRepos is false;


    public void Dispose()
    {
        _repos.Repos.CollectionChanged -= OnReposChanged;

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
}
