using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Games;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Repos;

/// <summary>
/// The repo's own pages that the overview opens, handed in by the repo page that owns the menu.
/// </summary>
public sealed record RepoOverviewLinks(Action ConnectGame, Action ConfigureGame);


/// <summary>
/// What the repo looks like from here: the repo itself, the game this machine has connected for it,
/// and who else is on which profile.
/// </summary>
/// <remarks>
/// The "This machine" card is the one place a connected game is shown and managed. Connecting,
/// configuring and disconnecting are offered only for a game whose adapter has local settings; one
/// without connects by itself and has nothing to configure.
/// </remarks>
public partial class RepoOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly RepoOverviewLinks _links;
    private readonly IProfileService _profileService;
    private readonly IRepoStore _repoStore;
    private readonly IMembershipService _membershipService;
    private readonly IDriftMonitor _driftMonitor;
    private readonly ISavegameBindingStore _bindingStore;
    private readonly IGameRepository _gameRepository;
    private readonly IProfileApplyService _applyService;
    private readonly IToastService _toasts;
    private readonly IModalService _modalService;
    private readonly ILogger<RepoOverviewPageViewModel> _logger;

    private readonly CancellationTokenSource _lifetime = new();

    private int? _fetchedMemberCount;

    /// <summary>
    /// The active profile last asked of the server, and its answer once it is in. See
    /// <see cref="LookUpProfile"/>.
    /// </summary>
    private ActiveProfile? _lookupFor;
    private ProfileLookup? _lookedUp;


    public RepoOverviewPageViewModel(
        Repo repo,
        RepoOverviewLinks links,
        IProfileService profileService,
        IRepoStore repoStore,
        IMembershipService membershipService,
        IDriftMonitor driftMonitor,
        ISavegameBindingStore bindingStore,
        IGameRepository gameRepository,
        IProfileApplyService applyService,
        IToastService toasts,
        IModalService modalService,
        ILogger<RepoOverviewPageViewModel> logger,
        FriendActivityListViewModel.Factory friendsFactory)
    {
        _repo = repo;
        _links = links;
        _profileService = profileService;
        _repoStore = repoStore;
        _membershipService = membershipService;
        _driftMonitor = driftMonitor;
        _bindingStore = bindingStore;
        _gameRepository = gameRepository;
        _applyService = applyService;
        _toasts = toasts;
        _modalService = modalService;
        _logger = logger;

        Friends = friendsFactory.Create(repo.Id);

        _repo.PropertyChanged += OnRepoPropertyChanged;
        _repo.Games.CollectionChanged += OnSourceCollectionChanged;
        _profileService.Profiles.CollectionChanged += OnSourceCollectionChanged;
        _driftMonitor.Changed += OnDriftChanged;

        // Which profile a game follows changes without the collection, the profiles or the drift
        // answer changing - deactivating a game with nothing wrong with it is exactly that.
        _gameRepository.GameChanged += OnGameChanged;

        // A savegame taken or handed back changes the holding line without touching a mod folder or
        // a profile, so nothing else here would say so.
        _bindingStore.BindingsChanged += OnBindingsChanged;

        RefreshGame();
    }


    public string RepoName => _repo.Name;

    public string GameName => _repo.Adapter.GameDisplayName;

    /// <summary>The game this machine has connected for this repo, or null where none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame))]
    [NotifyPropertyChangedFor(nameof(HasNoGame))]
    [NotifyPropertyChangedFor(nameof(CanCheckAgain))]
    [NotifyPropertyChangedFor(nameof(CanConnectGame))]
    [NotifyPropertyChangedFor(nameof(CanManageGame))]
    private GameOverviewViewModel? _game;

    public bool HasGame => Game is not null;

    public bool HasNoGame => Game is null;

    /// <summary>Who else in this repo is on which of its profiles, most recently active first.</summary>
    public FriendActivityListViewModel Friends { get; }

    public string MembershipSummary => _repo.MembershipLevel switch
    {
        RepoMembershipLevel.Admin => "You are an admin of this repo.",
        RepoMembershipLevel.Member => "You are a member of this repo.",
        _ => "You are a guest in this repo."
    };

    public string ProfileSummary => _profileService.Profiles.Count switch
    {
        0 => "No profiles yet.",
        1 => "1 profile.",
        var count => $"{count} profiles."
    };

    /// <summary>
    /// Whether this game has local settings, and so is connected, configured and disconnected by
    /// hand. One without connects by itself the moment it is found.
    /// </summary>
    public bool HasLocalSettings => GameRepository.ConnectsAutomatically(_repo.Adapter) is false;

    public string NotConnectedStatus => HasLocalSettings ? "Not connected" : "Not found on this machine";

    /// <summary>
    /// Whether looking again could change anything: always for a connected game, and for an
    /// unconnected one only where it connects by itself.
    /// </summary>
    public bool CanCheckAgain => HasGame || HasLocalSettings is false;

    public bool CanConnectGame => HasNoGame && HasLocalSettings;

    /// <summary>Whether the game can be configured and disconnected.</summary>
    public bool CanManageGame => HasGame && HasLocalSettings;

    /// <summary>Why the last thing the user did to the game here was refused, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameError))]
    private string? _gameError;

    public bool HasGameError => GameError is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemberSummary))]
    private string? _memberSummary;

    public bool HasMemberSummary => MemberSummary is not null;


    public void Dispose()
    {
        _repo.PropertyChanged -= OnRepoPropertyChanged;
        _repo.Games.CollectionChanged -= OnSourceCollectionChanged;
        _profileService.Profiles.CollectionChanged -= OnSourceCollectionChanged;
        _driftMonitor.Changed -= OnDriftChanged;
        _bindingStore.BindingsChanged -= OnBindingsChanged;
        _gameRepository.GameChanged -= OnGameChanged;

        // Cancelled but not disposed: a redraw already queued on the dispatcher may still read its token.
        _lifetime.Cancel();

        Friends.Dispose();
    }


    [RelayCommand]
    private void ConnectGame()
    {
        _links.ConnectGame();
    }

    [RelayCommand]
    private void ConfigureGame()
    {
        _links.ConfigureGame();
    }

    /// <summary>
    /// Looks for the game again: connects one that has turned up, catches a connected one up with
    /// wherever its folders are now, and reads the folders for drift.
    /// </summary>
    /// <remarks>
    /// A game still not found is not an error here: the card says so either way.
    /// </remarks>
    [RelayCommand]
    private async Task CheckAgain()
    {
        GameError = null;

        try
        {
            if (_gameRepository.Find(_repo.Scope) is Game game)
            {
                _gameRepository.RefreshTargets(game, _repo.Adapter);
            }
            else
            {
                _gameRepository.ConnectAutomatically(_repo.Adapter);
            }
        }
        catch (UserFriendlyException exception)
        {
            _logger.LogInformation("Game of repo {Repo} is still not readable: {Reason}", _repo.Id, exception.DeveloperMessage);
        }

        await _driftMonitor.CheckAsync();

        RefreshGame();
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        if (Game is not GameOverviewViewModel row)
        {
            return;
        }

        GameError = null;

        var modal = ConfirmationModalViewModel.ConfirmDisconnectGame(row.Name);

        await _modalService.Show(modal);

        if (modal.Result is false)
        {
            return;
        }

        try
        {
            _gameRepository.Delete(row.Game);
        }
        catch (UserFriendlyException exception)
        {
            _logger.LogInformation("Did not disconnect game {Game}: {Reason}", row.Game.Identity, exception.DeveloperMessage);
            GameError = exception.UserMessage;
        }
    }

    /// <summary>Stops the game following its profile and leaves the mod folders exactly as they are.</summary>
    [RelayCommand]
    private Task Deactivate(CancellationToken cancellationToken)
        => DeactivateAsync(clearMods: false, cancellationToken);

    /// <summary>Stops the game following its profile and takes every mod out of its folders too.</summary>
    [RelayCommand]
    private Task DeactivateAndClear(CancellationToken cancellationToken)
        => DeactivateAsync(clearMods: true, cancellationToken);

    /// <remarks>
    /// Not greyed while something else is applying or a savegame is held: the service refuses both
    /// with a sentence that says what to wait for or check in.
    /// </remarks>
    private async Task DeactivateAsync(bool clearMods, CancellationToken cancellationToken)
    {
        if (Game is not GameOverviewViewModel row)
        {
            return;
        }

        var outcome = await _applyService.DeactivateAsync(_repo, row.Game, clearMods, progress: null, cancellationToken);

        _toasts.Show(outcome.Message, outcome.ToastSeverity);

        await _driftMonitor.CheckAsync();

        RefreshGame();
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
            1 => "You are its only member.",
            var count => $"{count} members."
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
        OnPropertyChanged(nameof(CanCheckAgain));
        OnPropertyChanged(nameof(CanConnectGame));
        OnPropertyChanged(nameof(CanManageGame));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGame();

        OnPropertyChanged(nameof(ProfileSummary));
    }

    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor checks off the UI thread, and the card is bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGame);
    }

    /// <inheritdoc cref="OnDriftChanged"/>
    private void OnGameChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGame);
    }

    /// <summary>A savegame taken or handed back, here or anywhere else on this machine.</summary>
    /// <remarks>Dispatched for the same reason: a check-in completing is not guaranteed to be on the UI thread.</remarks>
    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGame);
    }

    /// <remarks>
    /// <see cref="Repo.Games"/> holds at most one game, filtered to the identity this repo is about.
    /// </remarks>
    private void RefreshGame()
    {
        if (_repo.Games.FirstOrDefault() is not Game game)
        {
            Game = null;

            return;
        }

        LookUpProfile(game.ActiveProfile);

        Game = new GameOverviewViewModel(
            game,
            GameInstallation.Read(game, _repo.Adapter, _logger),
            DescribeActiveProfile(game),
            DescribeHolding(game),
            [.. _driftMonitor.Drifted.Where(x => x.Game.Identity == game.Identity)]);
    }

    /// <remarks>
    /// A game is offered by every repo about the same game, so the profile it follows may belong to
    /// another repo than this one, and that repo may be gone. Gone is the same answer the drift check
    /// gives, from the same <see cref="IKnownRepos.IsGone"/>.
    /// </remarks>
    private GameDetailLine DescribeActiveProfile(Game game)
    {
        const string label = "Profile";

        if (game.ActiveProfile is not ActiveProfile active)
        {
            return new(label, "None");
        }

        var gone = new GameDetailLine(label, "No longer exists", IsProblem: true);

        if (_repoStore.IsGone(active.RepoId))
        {
            return gone;
        }

        var profile = _profileService.FindLive(active.RepoId, active.ProfileId);

        if (profile is null && _lookedUp is ProfileLookup answer && answer.For == active)
        {
            if (answer.Profile is null)
            {
                return gone;
            }

            profile = answer.Profile;
        }

        var name = profile?.Name ?? "A profile";

        // The repo's name only where it is another one. Not found is only possible before the repo
        // list has been read, and then there is nothing to name.
        var owner = active.RepoId == _repo.Id
            ? null
            : _repoStore.Repos.FirstOrDefault(x => x.Id == active.RepoId);

        string?[] parts =
        [
            owner is null ? name : $"{name} in {owner.Name}",
            profile?.ArchivedAt is null ? null : "archived",
            game.PinnedRevision is int pinned ? $"rev {pinned}" : null
        ];

        return new(label, string.Join(" · ", parts.OfType<string>()));
    }

    /// <summary>
    /// Asks the server for the active profile where the client holds no list it is in: another
    /// repo's, or an archived or deleted one of this repo. Once per active profile; the answer
    /// redraws the card.
    /// </summary>
    private void LookUpProfile(ActiveProfile? activeProfile)
    {
        if (activeProfile is not ActiveProfile active
            || _lookupFor == active
            || _repoStore.IsGone(active.RepoId)
            || _profileService.FindLive(active.RepoId, active.ProfileId) is not null)
        {
            return;
        }

        _lookupFor = active;

        // Observes its own failures, so nothing is lost by not awaiting it here.
        _ = LookUpProfileAsync(active, _lifetime.Token);
    }

    private async Task LookUpProfileAsync(ActiveProfile active, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _profileService.FindProfile(active.RepoId, active.ProfileId, cancellationToken);

            _lookedUp = new ProfileLookup(active, profile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            // The row says "A profile" rather than a name, which is all that is lost.
            _logger.LogWarning(exception, "Could not look up profile {Profile} of repo {Repo} for the overview.", active.ProfileId, active.RepoId);

            return;
        }

        RefreshGame();
    }

    /// <summary>
    /// What this game is holding, in one line, or null - nearly always - where it holds nothing with
    /// a mod list.
    /// </summary>
    /// <remarks>
    /// Asked of <see cref="SavegameHoldRules"/> rather than worked out here: the same questions decide
    /// whether the sync engine refuses an apply.
    /// </remarks>
    private string? DescribeHolding(Game game)
    {
        var held = _bindingStore.GetBindings(game.Identity);
        var claiming = held.FirstOrDefault(x => x.ProfileId is not null);

        if (claiming.ProfileId is not Guid profileId)
        {
            return null;
        }

        var profile = _profileService.Profiles.FirstOrDefault(x => x.Id == profileId)?.Name ?? "a mod list";

        return SavegameHoldRules.RequiredRevision(held, profileId) is int pinned
            ? $"Holding a savegame that runs on '{profile}' rev {pinned}. Check it in from Saves to move this game forward."
            : $"Holding a savegame that follows '{profile}'.";
    }


    /// <summary>What the server said about one active profile: the profile, or null where it does not exist.</summary>
    private sealed record ProfileLookup(ActiveProfile For, ProfileDto? Profile);


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoOverviewPageViewModel Create(Repo repo, RepoOverviewLinks links)
            => ActivatorUtilities.CreateInstance<RepoOverviewPageViewModel>(serviceProvider, repo, links);
    }
}
