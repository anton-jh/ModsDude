using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// One of a game's folders on the game's own page: where it is, and whether it still holds what was
/// applied to it.
/// </summary>
/// <param name="Name">
/// What to call this folder, or null where the game reaches one - which is nearly every game, and
/// which is why this page reads as a path and a sentence for almost everybody.
/// </param>
public sealed record GameTargetRow(string? Name, string Path, string DriftNote, string? LockedWarning)
{
    public bool HasName => Name is not null;
    public bool HasLockedWarning => LockedWarning is not null;
}


/// <summary>
/// The game's own page: which profile it follows, what it is holding, and a line for each folder it
/// reaches - with the settings on the Manage sub-page below.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reached rarely and on purpose.</b> Nothing in a normal evening opens it: activating a profile
/// is on the profile's page, taking and handing back a save is on the repo's, and the drift notice
/// carries whatever went wrong from wherever the user happens to be. What is left here is the three
/// things that are about the installation rather than about the sharing of it - its settings, its
/// folders and its slots.
/// </para>
/// <para>
/// <b>It used to carry a second copy of activation</b>: a profile dropdown spanning every repo that
/// shares this game's scope, an apply button beside it, and the hold rule greying the dropdown out.
/// All three were the profile page's controls seen from the other end, and two places to set one
/// thing is how they come to disagree. What survives of the hold is the sentence saying what is
/// checked out here, which is a fact about the game rather than a control on it.
/// </para>
/// </remarks>
public partial class GamePageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly Game _game;
    private readonly IProfilesClient _profilesClient;
    private readonly DriftService _driftService;
    private readonly DriftMonitor _driftMonitor;
    private readonly ISavegameService _savegameService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly ISavegamesClient _savegamesClient;
    private readonly ILogger<GamePageViewModel> _logger;

    /// <summary>
    /// What the game's active profile is called, and whether anything could find out.
    /// </summary>
    /// <remarks>
    /// One request, to the repo the active profile belongs to - which need not be the repo navigated
    /// in through, since a game is offered by every repo sharing its scope and holds one profile that
    /// may have come from any of them.
    /// </remarks>
    private ActiveProfileName _activeProfile = ActiveProfileName.Unknown;

    /// <summary>What the held savegames are called, so the status line can name one. Best effort.</summary>
    private IReadOnlyDictionary<Guid, string> _savegameNames = new Dictionary<Guid, string>();


    public GamePageViewModel(
        Repo repo,
        Game game,
        NavigationManager navigationManager,
        IProfilesClient profilesClient,
        DriftService driftService,
        DriftMonitor driftMonitor,
        ISavegameService savegameService,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        ISavegamesClient savegamesClient,
        ILogger<GamePageViewModel> logger,
        SyncPageViewModel.Factory syncPageViewModelFactory,
        GameSavegamesPageViewModel.Factory gameSavegamesPageViewModelFactory,
        GameSettingsPageViewModel.Factory gameSettingsPageViewModelFactory)
    {
        _repo = repo;
        _game = game;
        _profilesClient = profilesClient;
        _driftService = driftService;
        _driftMonitor = driftMonitor;
        _savegameService = savegameService;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _savegamesClient = savegamesClient;
        _logger = logger;

        // This page outlives a check-in, unlike every other surface that asks the hold question: the
        // slot list is its own sub-page, so checking a savegame in there leaves this shell standing
        // with a sentence about a hold that has ended.
        _bindingStore.BindingsChanged += OnBindingsChanged;

        GameName = game.Name;

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Sync", () => syncPageViewModelFactory.Create(repo, game))
                .WithIcon(MenuIcons.Sync)
        ];

        // The local half of savegames: the slot list, and the states a slot can be in that no server
        // knows about. Absent rather than closed where the game has no saves, for the same reason the
        // repo's Saves entry is.
        if (repo.Adapter.CanSupportSavegames)
        {
            MenuItems.Add(new MenuItemViewModel("Saves", () => gameSavegamesPageViewModelFactory.Create(repo, game))
                .WithIcon(MenuIcons.Saves));
        }

        MenuItems.Add(new MenuItemViewModel("Manage", () => gameSettingsPageViewModelFactory.Create(repo, game))
            .WithIcon(MenuIcons.Manage));

        NavManager.Selected = MenuItems.First();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }

    public string GameName { get; }

    /// <summary>
    /// One row per folder this game reaches, since each of them matches its profile or does not on
    /// its own. Empty for a game whose settings point at no folder, which is an ordinary answer.
    /// </summary>
    public ObservableCollection<GameTargetRow> Targets { get; } = [];

    public bool HasTargets => Targets.Count > 0;


    /// <summary>
    /// Which profile this game follows, said rather than chosen. Activation is on the profile's own
    /// page - see the remarks on this class.
    /// </summary>
    [ObservableProperty]
    private string _activeProfileNote = "Reading this game's profile...";

    /// <summary>
    /// What this game is holding, said in one Neutral line. Null - nearly always - where nothing
    /// with a profile is checked out here.
    /// </summary>
    /// <remarks>
    /// Neutral on purpose. Holding a past savegame is a state somebody chose and is playing in, not a
    /// problem with the game, so it reads like the folder paths beside it rather than like the
    /// locked-mod warning under them.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHoldingStatus))]
    private string? _holdingStatus;

    /// <summary>
    /// Why there is no folder row to read, where there is none. Null wherever <see cref="Targets"/>
    /// has something in it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTargetsNote))]
    private string? _noTargetsNote;


    public bool HasHoldingStatus => HoldingStatus is not null;
    public bool HasNoTargetsNote => NoTargetsNote is not null;


    protected override async Task InitAsync()
    {
        _activeProfile = await LoadActiveProfileNameAsync(CancellationToken.None);
        _savegameNames = await LoadSavegameNamesAsync(CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        RefreshHolding();
        RefreshDrift();
    }

    [RelayCommand]
    private async Task Recheck()
    {
        await _driftMonitor.CheckAsync();

        RefreshHolding();
        RefreshDrift();
    }

    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on.
    /// </summary>
    public void Dispose()
    {
        _bindingStore.BindingsChanged -= OnBindingsChanged;

        NavManager.Dispose();
    }


    /// <summary>
    /// A savegame taken or handed back, here or anywhere else on this machine.
    /// </summary>
    /// <remarks>
    /// Dispatched, because a check-in completing is not guaranteed to be on the UI thread and
    /// everything it changes is bound. The names are not re-read: a hold that has just ended needs no
    /// name, and one taken while this page stood open is named the next time it is opened - which is
    /// cheaper than a round trip per check-out and no less true.
    /// </remarks>
    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshHolding);
    }

    /// <summary>
    /// What the savegames checked out here demand of the mod folder, read off local state.
    /// </summary>
    /// <remarks>
    /// <b>Asked of <see cref="SavegameHoldRules"/> rather than worked out here.</b> The same
    /// questions decide whether the sync engine refuses an apply, and a second copy of them on this
    /// page is one that eventually disagrees with the surfaces that act on it.
    /// </remarks>
    private void RefreshHolding()
    {
        var held = _savegameService.GetBindings(_game);
        var claiming = held.FirstOrDefault(x => x.ProfileId is not null);

        if (claiming.ProfileId is not Guid profileId)
        {
            HoldingStatus = null;

            return;
        }

        // Quoted where it has a name and plain where it does not, so both readings are a sentence
        // rather than a name-shaped hole: the repo's list is a best-effort read, and a hold is a fact
        // about this machine whether or not it answered.
        var savegame = _savegameNames.GetValueOrDefault(claiming.SavegameId) is string named
            ? $"'{named}'"
            : "a savegame";

        var profile = _profileService.Profiles.FirstOrDefault(x => x.Id == profileId)?.Name ?? "its mod list";

        HoldingStatus = SavegameHoldRules.RequiredRevision(held, profileId) is int pinned
            ? $"Holding {profile} rev {pinned} for {savegame}. Check {savegame} in to move this game forward."
            : $"Holding {savegame}, which follows {profile}.";
    }

    /// <summary>
    /// One row per folder this game reaches, since each of them matches its profile or does not on
    /// its own. The folder is named only where there is more than one to tell apart.
    /// </summary>
    private void RefreshDrift()
    {
        ActiveProfileNote = _activeProfile.Describe();

        Targets.Clear();

        if (_game.Targets.Count == 0)
        {
            // No folder means no comparison, and there is no target to ask about one. The profile is
            // still worth a sentence, which is the line above this one.
            NoTargetsNote = "No mod folder is configured, so nothing is known about what is installed.";

            OnPropertyChanged(nameof(HasTargets));

            return;
        }

        NoTargetsNote = null;

        // Asked of the adapter, which this page always has: it is reached through a repo, so the
        // base settings that name the folders are right there. Only the drift notice is ever
        // without one.
        var names = TargetNames.Read(_game, _repo.Adapter, _logger);

        foreach (var target in _game.Targets)
        {
            var report = _driftService.Check(
                new ModTargetRef(_game.Identity, target.Key),
                _game.ActiveProfile,
                target.ModFolder,
                profileIsMissing: _activeProfile.IsGone);

            Targets.Add(new GameTargetRow(
                // Named by the one rule every folder name in the app follows, which answers null for
                // a game with a single folder.
                TargetNames.Distinguishing(target.Key, names.GetValueOrDefault(target.Key), _game.Targets.Count),
                target.ModFolder,
                Describe(report),
                DescribeLocked(report)));
        }

        OnPropertyChanged(nameof(HasTargets));
    }

    /// <summary>
    /// Named separately from the count: an unlocked mod at the wrong version is untidy, a locked map
    /// at the wrong version is a damaged savegame waiting to happen.
    /// </summary>
    private static string? DescribeLocked(DriftReport report)
    {
        var locked = report.LockedDrift.DistinctBy(x => x.ModId).ToList();

        return locked.Count > 0
            ? $"{string.Join(", ", locked.Select(x => $"'{x.DisplayName}'"))} " +
              "are locked and no longer match what was applied. Hosting a savegame on them may damage that save."
            : null;
    }

    private static string Describe(DriftReport report)
    {
        return report.Status switch
        {
            DriftStatus.InSync => "Matches what was last applied here.",
            DriftStatus.Drifted =>
                $"{report.DifferenceCount} differences from what was last applied here. Updating mods from inside the game looks like this.",
            DriftStatus.NeverSynced => "This profile has not been applied to this game yet.",
            // Told apart because only one of them is something that went wrong: an apply that did not
            // land leaves the folder on the list it was on, and a repointed folder is a settings edit
            // somebody made a moment ago.
            DriftStatus.NotApplied => report.AppliedProfileName is string applied
                ? $"Still on '{applied}'. This profile has not been applied here yet."
                : "Still on the profile it was last applied to, not this one.",
            DriftStatus.FolderRepointed => "The settings have been pointed somewhere else, and nothing has been applied there yet.",
            DriftStatus.NoActiveProfile => "No profile is set on this game yet.",
            DriftStatus.DanglingProfile => "The profile this game followed is gone. Activate another one from a profile's page.",
            // Unknown, not drifted: warning about mods that may be perfectly fine is worse than
            // saying nothing.
            DriftStatus.FolderUnreachable => "This folder cannot be reached right now, so nothing is known about it.",
            _ => ""
        };
    }

    /// <summary>
    /// What the game's active profile is called, in one request to the repo that owns it.
    /// </summary>
    /// <remarks>
    /// <b>Three answers, and the third is not the second.</b> A profile that is gone - deleted, or in
    /// a repo this user was removed from - is a state the drift check has its own status for, and
    /// this page is where somebody finds out. A request that simply failed is not that and must not
    /// be reported as it: the honest answer there is that nothing was found out, which leaves the
    /// folders' own comparison to stand on its own.
    /// </remarks>
    private async Task<ActiveProfileName> LoadActiveProfileNameAsync(CancellationToken cancellationToken)
    {
        if (_game.ActiveProfile is not ActiveProfile active)
        {
            return ActiveProfileName.None;
        }

        try
        {
            var profiles = await _profilesClient.GetProfilesV1Async(active.RepoId, cancellationToken);

            return profiles.FirstOrDefault(x => x.Id == active.ProfileId) is ProfileDto profile
                ? ActiveProfileName.Named(profile.Name, active.RepoId == _repo.Id)
                : ActiveProfileName.Gone;
        }
        catch (ApiException)
        {
            return ActiveProfileName.Unknown;
        }
    }

    /// <summary>
    /// What the savegames this game holds are called, for the one line that names one.
    /// </summary>
    /// <remarks>
    /// Best effort and absorbed on failure: a held binding is a fact about this machine and stays true
    /// whether or not the repo answers, so a name that could not be read costs a word in a sentence
    /// and nothing else. Archived savegames are read too - archiving does not release a hold.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadSavegameNamesAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();

        foreach (var repoId in _savegameService.GetBindings(_game).Select(x => x.RepoId).Distinct())
        {
            try
            {
                foreach (var savegame in await _savegamesClient.GetSavegamesV1Async(repoId, cancellationToken))
                {
                    names[savegame.Id] = savegame.Name;
                }

                foreach (var savegame in await _savegamesClient.GetArchivedSavegamesV1Async(repoId, cancellationToken))
                {
                    names[savegame.Id] = savegame.Name;
                }
            }
            catch (ApiException)
            {
                // One repo being unreadable is not a reason to name none of the others.
            }
        }

        return names;
    }


    /// <summary>
    /// The three answers to "which profile does this game follow", which are three different
    /// sentences and one of which is a drift status.
    /// </summary>
    private sealed record ActiveProfileName(string? Name, bool IsGone, bool IsFromThisRepo)
    {
        /// <summary>No profile has ever been activated on this game.</summary>
        public static ActiveProfileName None { get; } = new(null, false, false);

        /// <summary>The repo answered and does not have it: deleted, or this user was removed.</summary>
        public static ActiveProfileName Gone { get; } = new(null, true, false);

        /// <summary>Nothing was found out. Deliberately not <see cref="Gone"/>.</summary>
        public static ActiveProfileName Unknown { get; } = new(null, false, false);

        public static ActiveProfileName Named(string name, bool isFromThisRepo) => new(name, false, isFromThisRepo);


        public string Describe()
        {
            if (IsGone)
            {
                return "The profile this game followed is gone - it was deleted, or you were removed from its repo. Activate another one from a profile's page.";
            }

            if (Name is not string name)
            {
                return "No profile is set on this game. Open a profile and activate it here.";
            }

            // Which repo, where it is not this one: a game is offered by every repo sharing its
            // scope and holds one profile that may have come from any of them, so a bare name would
            // be one the user cannot find in the sidebar they are looking at.
            return IsFromThisRepo
                ? $"Follows '{name}'."
                : $"Follows '{name}', which belongs to another repo about this game.";
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public GamePageViewModel Create(Repo repo, Game game)
            => ActivatorUtilities.CreateInstance<GamePageViewModel>(serviceProvider, repo, game);
    }
}
