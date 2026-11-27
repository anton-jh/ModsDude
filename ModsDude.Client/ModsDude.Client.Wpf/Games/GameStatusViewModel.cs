using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Games;

/// <summary>
/// One game on this machine as a page shows it, kept current: whether it is connected and running,
/// the profile it follows, its folders and what it is holding. Shared by the repo Overview's card and
/// the game's own page, so the two cannot disagree.
/// </summary>
/// <remarks>
/// The adapter that reads the game's folders comes from a repo about it: the page's own repo where it
/// is opened from one, otherwise any repo this account is in for the game. With none, only what the
/// game's own record says is shown.
/// </remarks>
public sealed partial class GameStatusViewModel : ObservableObject, IDisposable
{
    private readonly GameIdentity _identity;
    private readonly Repo? _contextRepo;
    private readonly IGameRepository _gameRepository;
    private readonly IRepoStore _repoStore;
    private readonly IProfileStore _profileStore;
    private readonly IProfileService _profileService;
    private readonly IDriftMonitor _driftMonitor;
    private readonly ISavegameBindingStore _bindingStore;
    private readonly IGameRunningMonitor _runningGames;
    private readonly IProfileApplyService _applyService;
    private readonly IToastService _toasts;
    private readonly ILogger<GameStatusViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The active profile last asked of the server, and its answer once it is in. See <see cref="LookUpProfile"/>.</summary>
    private ActiveProfile? _lookupFor;
    private ProfileLookup? _lookedUp;


    /// <param name="contextRepo">The repo the page belongs to, or null on a page about the game alone.</param>
    public GameStatusViewModel(
        GameIdentity identity,
        Repo? contextRepo,
        IGameRepository gameRepository,
        IRepoStore repoStore,
        IProfileStore profileStore,
        IProfileService profileService,
        IDriftMonitor driftMonitor,
        ISavegameBindingStore bindingStore,
        IGameRunningMonitor runningGames,
        IProfileApplyService applyService,
        IToastService toasts,
        ILogger<GameStatusViewModel> logger)
    {
        _identity = identity;
        _contextRepo = contextRepo;
        _gameRepository = gameRepository;
        _repoStore = repoStore;
        _profileStore = profileStore;
        _profileService = profileService;
        _driftMonitor = driftMonitor;
        _bindingStore = bindingStore;
        _runningGames = runningGames;
        _applyService = applyService;
        _toasts = toasts;
        _logger = logger;

        _gameRepository.Games.CollectionChanged += OnCollectionChanged;
        _repoStore.Repos.CollectionChanged += OnCollectionChanged;

        // Which profile a game follows changes without the collection changing - deactivating a game
        // with nothing wrong with it is exactly that.
        _gameRepository.GameChanged += OnChanged;
        _driftMonitor.Changed += OnChanged;
        _runningGames.Changed += OnChanged;

        // A savegame taken or handed back changes the holding line without touching a mod folder or
        // a profile, so nothing else here would say so.
        _bindingStore.BindingsChanged += OnChanged;
        _profileStore.Changed += OnProfilesChanged;

        if (_contextRepo is not null)
        {
            // The adapter is replaced whenever the repo's base settings are.
            _contextRepo.PropertyChanged += OnContextRepoChanged;
        }

        Refresh();
    }


    public GameIdentity Identity => _identity;

    /// <summary>The game as it stands, or null where it is not connected on this machine.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(CanDeactivate))]
    private GameOverviewViewModel? _game;

    public bool IsConnected => Game is not null;

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>The repo whose adapter reads this game, or null where no repo this account is in is about it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeactivate))]
    private Repo? _adapterRepo;

    /// <summary>Whether there is a profile to stop following, and a repo to do it through.</summary>
    public bool CanDeactivate => Game is { HasActiveProfile: true } && AdapterRepo is not null;


    public void Dispose()
    {
        _gameRepository.Games.CollectionChanged -= OnCollectionChanged;
        _repoStore.Repos.CollectionChanged -= OnCollectionChanged;
        _gameRepository.GameChanged -= OnChanged;
        _driftMonitor.Changed -= OnChanged;
        _runningGames.Changed -= OnChanged;
        _bindingStore.BindingsChanged -= OnChanged;
        _profileStore.Changed -= OnProfilesChanged;

        if (_contextRepo is not null)
        {
            _contextRepo.PropertyChanged -= OnContextRepoChanged;
        }

        // Cancelled but not disposed: a redraw already queued on the dispatcher may still read its token.
        _lifetime.Cancel();
    }

    /// <summary>Reads everything again. On the UI thread.</summary>
    public void Refresh()
    {
        AdapterRepo = _contextRepo ?? _repoStore.Repos
            .Where(x => x.Scope == _identity)
            .OrderBy(x => x.Id)
            .FirstOrDefault();

        IsRunning = _runningGames.IsRunning(_identity);

        if (_gameRepository.Find(_identity) is not Game game)
        {
            Game = null;

            return;
        }

        LookUpProfile(game.ActiveProfile);

        Game = new GameOverviewViewModel(
            game,
            AdapterRepo is Repo repo ? GameInstallation.Read(game, repo.Adapter, _logger) : GameInstallation.Recorded(game),
            DescribeActiveProfile(game),
            DescribeHolding(game),
            [.. _driftMonitor.Drifted.Where(x => x.Game.Identity == game.Identity)]);
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
        if (Game is not GameOverviewViewModel row || AdapterRepo is not Repo repo)
        {
            return;
        }

        var outcome = await _applyService.DeactivateAsync(repo, row.Game, clearMods, progress: null, cancellationToken);

        _toasts.Show(outcome.Message, outcome.ToastSeverity);

        await _driftMonitor.CheckAsync();

        Refresh();
    }

    /// <remarks>
    /// A game is offered by every repo about the same game, so the profile it follows may belong to
    /// another repo than the page's, and that repo may be gone. Gone is the same answer the drift check
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

        var live = _profileStore.Find(active.RepoId, active.ProfileId);
        var name = live?.Name;
        var archived = false;

        if (live is null && _lookedUp is ProfileLookup answer && answer.For == active)
        {
            if (answer.Profile is null)
            {
                return gone;
            }

            name = answer.Profile.Name;
            archived = answer.Profile.ArchivedAt is not null;
        }

        name ??= "A profile";

        // The repo's name wherever it is not the page's own. Not found is only possible before the
        // repo list has been read, and then there is nothing to name.
        var owner = active.RepoId == _contextRepo?.Id
            ? null
            : _repoStore.Repos.FirstOrDefault(x => x.Id == active.RepoId);

        string?[] parts =
        [
            owner is null ? name : $"{name} in {owner.Name}",
            archived ? "archived" : null,
            game.PinnedRevision is int pinned ? $"revision {pinned}" : null
        ];

        return new(label, string.Join(" · ", parts.OfType<string>()));
    }

    /// <summary>
    /// Asks the server for the active profile where the client holds no list it is in: another
    /// repo's, or an archived or deleted one. Once per active profile; the answer redraws the card.
    /// </summary>
    private void LookUpProfile(ActiveProfile? activeProfile)
    {
        if (activeProfile is not ActiveProfile active
            || _lookupFor == active
            || _repoStore.IsGone(active.RepoId)
            || _profileStore.Find(active.RepoId, active.ProfileId) is not null)
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
            _logger.LogWarning(exception, "Could not look up profile {Profile} of repo {Repo} for game {Game}.", active.ProfileId, active.RepoId, _identity);

            return;
        }

        Refresh();
    }

    /// <summary>
    /// The savegame checked out on this game, in one line, or null (nearly always) where none follows
    /// a profile.
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

        var profile = _profileStore.Find(claiming.RepoId, profileId)?.Name is string name ? $"'{name}'" : "a profile";

        return SavegameHoldRules.RequiredRevision(held, profileId) is int pinned
            ? $"A savegame checked out here uses {profile} revision {pinned}. Check it in to update this game."
            : $"A savegame checked out here uses {profile}";
    }

    /// <remarks>Every source here may say so off the UI thread, and the card is bound.</remarks>
    private void Post()
        => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_lifetime.IsCancellationRequested is false)
            {
                Refresh();
            }
        });

    private void OnChanged(object? sender, EventArgs e) => Post();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Post();

    private void OnProfilesChanged(Guid repoId) => Post();

    private void OnContextRepoChanged(object? sender, PropertyChangedEventArgs e) => Post();


    /// <summary>What the server said about one active profile: the profile, or null where it does not exist.</summary>
    private sealed record ProfileLookup(ActiveProfile For, ProfileDto? Profile);


    /// <remarks>
    /// Built by hand rather than through <c>ActivatorUtilities</c>, which cannot hand a constructor a
    /// null argument.
    /// </remarks>
    public sealed class Factory(IServiceProvider serviceProvider)
    {
        /// <inheritdoc cref="GameStatusViewModel(GameIdentity, Repo?, IGameRepository, IRepoStore, IProfileStore, IProfileService, IDriftMonitor, ISavegameBindingStore, IGameRunningMonitor, IProfileApplyService, IToastService, ILogger{GameStatusViewModel})"/>
        public GameStatusViewModel Create(GameIdentity identity, Repo? contextRepo)
            => new(
                identity,
                contextRepo,
                serviceProvider.GetRequiredService<IGameRepository>(),
                serviceProvider.GetRequiredService<IRepoStore>(),
                serviceProvider.GetRequiredService<IProfileStore>(),
                serviceProvider.GetRequiredService<IProfileService>(),
                serviceProvider.GetRequiredService<IDriftMonitor>(),
                serviceProvider.GetRequiredService<ISavegameBindingStore>(),
                serviceProvider.GetRequiredService<IGameRunningMonitor>(),
                serviceProvider.GetRequiredService<IProfileApplyService>(),
                serviceProvider.GetRequiredService<IToastService>(),
                serviceProvider.GetRequiredService<ILogger<GameStatusViewModel>>());
    }
}
