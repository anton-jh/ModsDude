using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Repos;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Games;

/// <summary>One savegame the game holds, as its page lists it.</summary>
/// <param name="IsGone">Whether its repo is not one of this account's, so it can only be stopped tracking.</param>
/// <param name="HasUncheckedInPlay">Whether it has been played here since it was checked out.</param>
public sealed record GameHeldSavegameRow(
    Guid SavegameId,
    Guid RepoId,
    string Name,
    string RepoName,
    bool IsGone,
    bool HasUncheckedInPlay)
{
    public bool IsLive => IsGone is false;
}


/// <summary>A repo this account is in that is about the game.</summary>
public sealed record GameRepoLink(Guid RepoId, string Name);


/// <summary>
/// Everything about one game on this machine: how it stands, where it is installed, what it is
/// holding and which repos are about it. The one place a game is configured, checked again and
/// disconnected.
/// </summary>
/// <remarks>
/// Opened from Home, from the heading of a repo Overview's "This machine" card and from notices. It
/// needs no repo of its own, so a game whose repos are all gone can still be let go of here.
/// </remarks>
public sealed partial class GamePageViewModel : PageViewModel, IDisposable
{
    private readonly IGameRepository _gameRepository;
    private readonly IRepoStore _repoStore;
    private readonly ISavegameBindingStore _bindingStore;
    private readonly IGoneRepos _goneRepos;
    private readonly IHeldSavegameNames _heldNames;
    private readonly IDriftMonitor _driftMonitor;
    private readonly IGameDisconnectFlow _disconnectFlow;
    private readonly ISavegameDisconnectFlow _savegameDisconnectFlow;
    private readonly IShellNavigationService _navigation;
    private readonly INavigationLockService _navigationLock;
    private readonly IToastService _toasts;
    private readonly ILogger<GamePageViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LatestLoad _namesLoad;

    /// <summary>The adapter the settings form was built from, or null where the page has no form.</summary>
    private readonly IBaseGameAdapter? _settingsAdapter;

    /// <summary>What the game was called when the page opened, for after it is disconnected.</summary>
    private readonly string _openedName;

    private IReadOnlyDictionary<Guid, HeldSavegameName> _savegameNames = new Dictionary<Guid, HeldSavegameName>();


    public GamePageViewModel(
        GameStatusViewModel status,
        IAsyncRelayCommand back,
        IGameRepository gameRepository,
        IRepoStore repoStore,
        ISavegameBindingStore bindingStore,
        IGoneRepos goneRepos,
        IHeldSavegameNames heldNames,
        IDriftMonitor driftMonitor,
        IGameDisconnectFlow disconnectFlow,
        ISavegameDisconnectFlow savegameDisconnectFlow,
        IShellNavigationService navigation,
        IFilePickerService filePicker,
        INavigationLockService navigationLock,
        IToastService toasts,
        ILogger<GamePageViewModel> logger)
    {
        Status = status;
        BackCommand = back;
        _gameRepository = gameRepository;
        _repoStore = repoStore;
        _bindingStore = bindingStore;
        _goneRepos = goneRepos;
        _heldNames = heldNames;
        _driftMonitor = driftMonitor;
        _disconnectFlow = disconnectFlow;
        _savegameDisconnectFlow = savegameDisconnectFlow;
        _navigation = navigation;
        _navigationLock = navigationLock;
        _toasts = toasts;
        _logger = logger;
        _namesLoad = new(_ => { }, logger, _lifetime.Token);

        var game = gameRepository.Find(status.Identity);

        _openedName = game?.Name ?? status.Identity.ToString();

        // A game that connects by itself has no settings anybody fills in, and the adapter that reads
        // a form comes from a repo about the game.
        if (game is not null
            && status.AdapterRepo is Repo repo
            && GameRepository.ConnectsAutomatically(repo.Adapter) is false)
        {
            _settingsAdapter = repo.Adapter;
            Settings = new DynamicFormViewModel(true, game.GetLocalSettings(repo.Adapter), filePicker);
            Settings.PropertyChanged += OnSettingsChanged;
        }

        Status.PropertyChanged += OnStatusChanged;
        _bindingStore.BindingsChanged += OnBindingsChanged;

        Rebuild();
    }


    public GameStatusViewModel Status { get; }

    public IAsyncRelayCommand BackCommand { get; }

    public string Title => Status.Game?.Name ?? _openedName;

    /// <summary>Where the game is installed on this machine, or null where there is nothing to configure here.</summary>
    public DynamicFormViewModel? Settings { get; }

    public bool HasSettings => Settings is not null && Status.IsConnected;

    /// <summary>Why the settings were not saved, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingsError))]
    private string? _settingsError;

    public bool HasSettingsError => SettingsError is not null;

    /// <summary>Why the last thing done to the game here was refused, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameError))]
    private string? _gameError;

    public bool HasGameError => GameError is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeldSavegames))]
    private IReadOnlyList<GameHeldSavegameRow> _heldSavegames = [];

    public bool HasHeldSavegames => HeldSavegames.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRepos))]
    private IReadOnlyList<GameRepoLink> _repos = [];

    public bool HasNoRepos => Repos.Count == 0;

    /// <summary>Whether looking again can be done: it asks the adapter, which comes from a repo.</summary>
    public bool CanCheckAgain => Status.IsConnected && Status.AdapterRepo is not null;

    /// <summary>
    /// Whether the game can be disconnected: one with settings somebody filled in, or one no repo of
    /// this account's is about, which nothing would connect again by itself.
    /// </summary>
    public bool CanDisconnect => Status.IsConnected
        && (Status.AdapterRepo is not Repo repo || GameRepository.ConnectsAutomatically(repo.Adapter) is false);


    protected override Task InitAsync()
        => Application.Current.Dispatcher.InvokeAsync(ReadSavegameNamesAsync).Task.Unwrap();

    public void Dispose()
    {
        Status.PropertyChanged -= OnStatusChanged;
        _bindingStore.BindingsChanged -= OnBindingsChanged;

        if (Settings is not null)
        {
            Settings.PropertyChanged -= OnSettingsChanged;
            Settings.Dispose();
        }

        _navigationLock.ReleaseLock(this);

        // Cancelled but not disposed: a redraw already queued on the dispatcher may still read its token.
        _lifetime.Cancel();

        Status.Dispose();
    }


    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private void SaveSettings()
    {
        SettingsError = null;

        if (Settings is null || _settingsAdapter is null || Status.Game?.Game is not Game game)
        {
            return;
        }

        var errors = GetValidationErrors(Settings, _settingsAdapter, game);

        if (errors.Count > 0)
        {
            SettingsError = string.Join(" ", errors);

            return;
        }

        var outcome = _gameRepository.Update(game, _settingsAdapter, Settings.ExtractResults());

        SettingsError = outcome switch
        {
            TargetRefresh.Busy => $"'{game.Name}' is busy. Try again once it has finished.",
            TargetRefresh.HoldsSavegame => SavegameRefusals.GameHoldsSavegame(game.Name, _bindingStore.GetBindings(game.Identity), _goneRepos),
            _ => null
        };

        if (SettingsError is not null)
        {
            _logger.LogInformation("Settings of {Game} were not saved: {Outcome}", game.Identity, outcome);

            return;
        }

        Settings.MarkSaved();

        _toasts.Show($"Saved the settings for {game.Name}");
    }

    private bool CanSaveSettings() => Settings is { HasChanges: true };

    /// <summary>
    /// Looks for the game again: catches it up with wherever its folders are now, and reads them for drift.
    /// </summary>
    [RelayCommand]
    private async Task CheckAgain()
    {
        GameError = null;

        if (Status.Game?.Game is not Game game || Status.AdapterRepo is not Repo repo)
        {
            return;
        }

        try
        {
            GameError = _gameRepository.RefreshTargets(game, repo.Adapter) switch
            {
                TargetRefresh.Busy => $"'{game.Name}' is busy. Try again once it has finished.",
                TargetRefresh.HoldsSavegame => SavegameRefusals.GameHoldsSavegame(game.Name, _bindingStore.GetBindings(game.Identity), _goneRepos),
                _ => null
            };
        }
        catch (UserFriendlyException exception)
        {
            // A game still not readable is not an error here: the card says so either way.
            _logger.LogInformation("Game {Game} is still not readable: {Reason}", game.Identity, exception.DeveloperMessage);
        }

        await _driftMonitor.CheckAsync();

        Status.Refresh();
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        GameError = null;

        if (Status.Game?.Game is not Game game)
        {
            return;
        }

        GameError = await _disconnectFlow.DisconnectAsync(game);

        if (GameError is null && _gameRepository.Find(game.Identity) is null)
        {
            await BackCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task OpenSavegame(GameHeldSavegameRow row)
    {
        GameError = null;

        if (await _navigation.GoToAsync(row.RepoId, new RepoDestination.Savegame(row.SavegameId)) is false)
        {
            GameError = "Could not open the savegame.";
        }
    }

    /// <summary>
    /// Asks again whether the repo is gone rather than trusting the row, which may have been drawn
    /// before the server stopped answering.
    /// </summary>
    [RelayCommand]
    private async Task StopTracking(GameHeldSavegameRow row)
    {
        GameError = null;

        if (Status.Game?.Game is not Game game)
        {
            return;
        }

        if (_goneRepos.IsGone(row.RepoId) is false)
        {
            GameError = "Could not check the repo. Try again in a moment.";

            return;
        }

        await _savegameDisconnectFlow.DisconnectAsync(
            game,
            row.SavegameId,
            row.Name,
            new SavegameDisconnectReason.RepoGone(row.HasUncheckedInPlay));
    }

    [RelayCommand]
    private Task OpenRepo(GameRepoLink link)
        => _navigation.GoToAsync(link.RepoId, new RepoDestination.Section(RepoSection.Overview));


    /// <summary>Draws the savegame and repo lists again from the stores. On the UI thread.</summary>
    private void Rebuild()
    {
        var drifted = _driftMonitor.Drifted;

        HeldSavegames = Status.IsConnected
            ? [.. _bindingStore.GetBindings(Status.Identity)
                .OrderBy(x => x.Slot.ToString(), StringComparer.Ordinal)
                .Select(x => Row(x, drifted))]
            : [];

        Repos = [.. _repoStore.Repos
            .Where(x => x.Scope == Status.Identity)
            .OrderBy(x => x.Name, NaturalOrder.Comparer)
            .ThenBy(x => x.Id)
            .Select(x => new GameRepoLink(x.Id, x.Name))];
    }

    private GameHeldSavegameRow Row(SavegameCheckoutBinding binding, IReadOnlyList<TargetDrift> drifted)
    {
        var isGone = _goneRepos.IsGone(binding.RepoId);

        var name = _savegameNames.GetValueOrDefault(binding.SavegameId)?.Name
            ?? HeldSavegameDrift.SlotName(drifted, binding.SavegameId)
            ?? $"Slot '{binding.Slot.Slot}'";

        var repoName = isGone
            ? "Repo you are not in"
            : _repoStore.Repos.FirstOrDefault(x => x.Id == binding.RepoId)?.Name ?? "A repo";

        return new GameHeldSavegameRow(
            binding.SavegameId,
            binding.RepoId,
            name,
            repoName,
            isGone,
            HeldSavegameDrift.HasUncheckedInPlay(drifted, binding.SavegameId));
    }

    /// <summary>Reads what the held savegames are called. On the UI thread.</summary>
    private Task ReadSavegameNamesAsync()
    {
        if (_lifetime.IsCancellationRequested || _gameRepository.Find(Status.Identity) is not Game game)
        {
            return Task.CompletedTask;
        }

        return _namesLoad.RunAsync(
            cancellationToken => _heldNames.ReadAsync(game, null, cancellationToken),
            names =>
            {
                _savegameNames = names;
                Rebuild();
            },
            exception =>
            {
                _logger.LogInformation(exception, "Could not read the names of the savegames held by {Game}.", game.Identity);

                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Checked across every game, since two of them can name the same folder and only one can own
    /// it, and across this one's own targets. Only asked of settings that are valid in their own
    /// right - the adapter refuses to hydrate anything else.
    /// </summary>
    private List<string> GetValidationErrors(DynamicFormViewModel settings, IBaseGameAdapter adapter, Game game)
    {
        var errors = settings.GetValidationErrors();

        if (settings.IsValid && _gameRepository.FindFolderConflict(adapter, settings.ExtractResults(), game.Identity) is FolderClaim claim)
        {
            errors.Add(claim.Describe());
        }

        return errors;
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(GameStatusViewModel.Game) or nameof(GameStatusViewModel.AdapterRepo)))
        {
            return;
        }

        // A new answer from the status is the moment everything it draws from may have moved: the
        // drift check, the repo list, which repos are gone.
        Rebuild();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(CanCheckAgain));
        OnPropertyChanged(nameof(CanDisconnect));
    }

    /// <remarks>Async void as an event handler: the load it awaits observes its own failures.</remarks>
    private async void OnBindingsChanged(object? sender, EventArgs e)
    {
        await await Application.Current.Dispatcher.InvokeAsync(ReadSavegameNamesAsync);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DynamicFormViewModel.HasChanges))
        {
            SaveSettingsCommand.NotifyCanExecuteChanged();
            _navigationLock.SetLock(this, Settings?.HasChanges is true);
        }
    }


    /// <remarks>
    /// The status is built first by its own factory, which takes the nullable repo that
    /// <c>ActivatorUtilities</c> cannot pass.
    /// </remarks>
    public sealed class Factory(IServiceProvider serviceProvider, GameStatusViewModel.Factory statusFactory)
    {
        /// <param name="contextRepo">The repo whose page this opens under, or null when opened from Home or a notice.</param>
        public GamePageViewModel Create(GameIdentity identity, Repo? contextRepo, IAsyncRelayCommand back)
            => ActivatorUtilities.CreateInstance<GamePageViewModel>(serviceProvider, statusFactory.Create(identity, contextRepo), back);
    }
}
