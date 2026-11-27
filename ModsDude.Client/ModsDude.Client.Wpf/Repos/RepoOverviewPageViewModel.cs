using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Games;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.Repos;

/// <summary>
/// The repo's own pages that the overview opens, handed in by the repo page that owns the menu.
/// </summary>
public sealed record RepoOverviewLinks(Action ConnectGame, Action OpenGame);


/// <summary>
/// What the repo looks like from here: the repo itself, the game this machine has connected for it,
/// and who else is on which profile.
/// </summary>
/// <remarks>
/// The "This machine" card summarises the game and leads to its own page, where it is configured,
/// checked again and disconnected. Connecting is offered only for a game whose adapter has local
/// settings; one without connects by itself.
/// </remarks>
public partial class RepoOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly RepoOverviewLinks _links;
    private readonly IProfileStore _profileStore;
    private readonly IMembershipService _membershipService;
    private readonly IDriftMonitor _driftMonitor;
    private readonly IGameRepository _gameRepository;
    private readonly ILogger<RepoOverviewPageViewModel> _logger;

    private int? _fetchedMemberCount;


    public RepoOverviewPageViewModel(
        Repo repo,
        RepoOverviewLinks links,
        IProfileStore profileStore,
        IMembershipService membershipService,
        IDriftMonitor driftMonitor,
        IGameRepository gameRepository,
        GameStatusViewModel.Factory gameStatusFactory,
        ILogger<RepoOverviewPageViewModel> logger,
        FriendActivityListViewModel.Factory friendsFactory)
    {
        _repo = repo;
        _links = links;
        _profileStore = profileStore;
        _membershipService = membershipService;
        _driftMonitor = driftMonitor;
        _gameRepository = gameRepository;
        _logger = logger;

        Friends = friendsFactory.Create(repo.Id);
        Game = gameStatusFactory.Create(repo.Scope, repo);

        _repo.PropertyChanged += OnRepoPropertyChanged;
        _profileStore.Live(repo.Id).CollectionChanged += OnProfilesChanged;
        Game.PropertyChanged += OnGameChanged;
    }


    public string RepoName => _repo.Name;

    public string GameName => _repo.Adapter.GameDisplayName;

    /// <summary>The game this machine has connected for this repo, as its card shows it.</summary>
    public GameStatusViewModel Game { get; }

    /// <summary>Who else in this repo is on which of its profiles, most recently active first.</summary>
    public FriendActivityListViewModel Friends { get; }

    public string MembershipSummary => _repo.MembershipLevel switch
    {
        RepoMembershipLevel.Admin => "Your level: Admin",
        RepoMembershipLevel.Member => "Your level: Member",
        _ => "Your level: Guest"
    };

    public string ProfileSummary => _profileStore.Live(_repo.Id).Count switch
    {
        0 => "No profiles yet",
        1 => "1 profile",
        var count => $"{count} profiles"
    };

    /// <summary>
    /// Whether this game has local settings, and so is connected by hand. One without connects by
    /// itself the moment it is found.
    /// </summary>
    public bool HasLocalSettings => GameRepository.ConnectsAutomatically(_repo.Adapter) is false;

    public string NotConnectedStatus => HasLocalSettings ? "Not connected" : "Not found on this machine";

    public bool CanConnectGame => Game.IsConnected is false && HasLocalSettings;

    /// <summary>Whether looking for a game that connects by itself could find it now.</summary>
    public bool CanLookAgain => Game.IsConnected is false && HasLocalSettings is false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemberSummary))]
    private string? _memberSummary;

    public bool HasMemberSummary => MemberSummary is not null;


    public void Dispose()
    {
        _repo.PropertyChanged -= OnRepoPropertyChanged;
        _profileStore.Live(_repo.Id).CollectionChanged -= OnProfilesChanged;
        Game.PropertyChanged -= OnGameChanged;

        Game.Dispose();
        Friends.Dispose();
    }


    [RelayCommand]
    private void ConnectGame()
    {
        _links.ConnectGame();
    }

    [RelayCommand]
    private void OpenGame()
    {
        _links.OpenGame();
    }

    /// <summary>Looks for a game that connects by itself, and reads its folders for drift once found.</summary>
    /// <remarks>A game still not found is not an error here: the card says so either way.</remarks>
    [RelayCommand]
    private async Task LookAgain()
    {
        try
        {
            _gameRepository.ConnectAutomatically(_repo.Adapter);
        }
        catch (UserFriendlyException exception)
        {
            _logger.LogInformation("Game of repo {Repo} is still not readable: {Reason}", _repo.Id, exception.DeveloperMessage);
        }

        await _driftMonitor.CheckAsync();

        Game.Refresh();
    }


    protected override async Task InitAsync()
    {
        // First and on its own: it says its own failure on the card, and the member count below it
        // must not wait on it or be lost to it.
        _ = Friends.RefreshAsync();

        // Reading the member list needs Member, so for a guest there is simply nothing to say.
        if (_repo.MembershipLevel < RepoMembershipLevel.Member)
        {
            return;
        }

        _fetchedMemberCount = (await _membershipService.GetMembers(_repo.Id, CancellationToken.None)).Count;
    }

    protected override void OnInitCompleted()
    {
        MemberSummary = _fetchedMemberCount switch
        {
            null => null,
            1 => "1 member",
            var count => $"{count} members"
        };
    }


    private void OnRepoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RepoName));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(MembershipSummary));

        // The adapter is replaced whenever the repo's base settings are.
        OnPropertyChanged(nameof(HasLocalSettings));
        OnPropertyChanged(nameof(NotConnectedStatus));
        OnPropertyChanged(nameof(CanConnectGame));
        OnPropertyChanged(nameof(CanLookAgain));
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ProfileSummary));
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameStatusViewModel.IsConnected))
        {
            OnPropertyChanged(nameof(CanConnectGame));
            OnPropertyChanged(nameof(CanLookAgain));
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoOverviewPageViewModel Create(Repo repo, RepoOverviewLinks links)
            => ActivatorUtilities.CreateInstance<RepoOverviewPageViewModel>(serviceProvider, repo, links);
    }
}
