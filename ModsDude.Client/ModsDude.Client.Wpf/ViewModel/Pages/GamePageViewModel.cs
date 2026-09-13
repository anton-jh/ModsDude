using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
/// The game's own page: which profile it follows, whether its mod folder still matches, and the
/// one action that fixes it - with the settings and the name on the Manage sub-page below.
/// </summary>
/// <remarks>
/// <para>
/// Activation lives here because this is the end of it where the target is fixed and the profile is
/// chosen. The choice spans every repo sharing this game's scope rather than the repo the user
/// navigated in through, since the game is shared across all of them and holds one active profile
/// that may have come from any.
/// </para>
/// <para>
/// The picker used to sit on Manage as well. It does not any more - two places to set one thing is
/// how they disagree.
/// </para>
/// <para>
/// <b>A held savegame changes what both controls mean.</b> One with a profile claims this mod folder,
/// so the picker is disabled rather than offering a switch the apply table refuses; and a <em>past</em>
/// one pins the folder to its own revision, so the button stops meaning "put this on the profile's
/// latest" and says which revision it is repairing to instead. Both are the same rule read from the
/// game's end - see docs/10-savegame-profile-binding.md#game-page.
/// </para>
/// </remarks>
public partial class GamePageViewModel : PageViewModel, IDisposable
{
    private readonly Game _game;
    private readonly RepoRepository _repoRepository;
    private readonly IProfilesClient _profilesClient;
    private readonly DriftService _driftService;
    private readonly DriftMonitor _driftMonitor;
    private readonly ProfileApplyService _applyService;
    private readonly ISavegameService _savegameService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly ISavegamesClient _savegamesClient;

    private IReadOnlyList<InstanceProfileOptionViewModel> _fetchedOptions = [];

    /// <summary>What the held savegames are called, so the status line can name one. Best effort.</summary>
    private IReadOnlyDictionary<Guid, string> _savegameNames = new Dictionary<Guid, string>();


    public GamePageViewModel(
        Repo repo,
        Game game,
        NavigationManager navigationManager,
        RepoRepository repoRepository,
        IProfilesClient profilesClient,
        DriftService driftService,
        DriftMonitor driftMonitor,
        ProfileApplyService applyService,
        ISavegameService savegameService,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        ISavegamesClient savegamesClient,
        SyncPageViewModel.Factory syncPageViewModelFactory,
        GameSavegamesPageViewModel.Factory gameSavegamesPageViewModelFactory,
        GameSettingsPageViewModel.Factory gameSettingsPageViewModelFactory)
    {
        _game = game;
        _repoRepository = repoRepository;
        _profilesClient = profilesClient;
        _driftService = driftService;
        _driftMonitor = driftMonitor;
        _applyService = applyService;
        _savegameService = savegameService;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _savegamesClient = savegamesClient;

        // This page outlives a check-in, unlike every other surface that asks the hold question: the
        // slot list is its own sub-page, so checking a savegame in there leaves this shell standing
        // with a disabled dropdown and a Re-apply rev 4 that are both about a hold that has ended.
        _bindingStore.BindingsChanged += OnBindingsChanged;

        GameName = game.Name;
        // Joined for now: slice 5 turns this into the target list it really is.
        ModFolder = game.Targets.Count > 0 ? string.Join(", ", game.Targets.Select(x => x.ModFolder)) : "No mod folder configured";

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Sync", () => syncPageViewModelFactory.Create(repo, game))
                .WithIcon(MenuIcons.Sync)
        ];

        // The local half of savegames: the slot list, and the one verb - publish - that is inherently
        // about a slot. Absent rather than closed where the game has no saves, for the same reason the
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

    public ObservableCollection<InstanceProfileOptionViewModel> Profiles { get; } = [];

    public string GameName { get; }
    public string ModFolder { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private InstanceProfileOptionViewModel? _selectedProfile;

    /// <summary>
    /// What this game is holding and what that demands of its mod folder, said in one Neutral
    /// line. Null - nearly always - where nothing with a profile is checked out here.
    /// </summary>
    /// <remarks>
    /// Neutral on purpose. Holding a past savegame is a state somebody chose and is playing in, not a
    /// problem with the game, so it reads like the mod-folder path underneath it rather than like
    /// the locked-mod warning above it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHoldingStatus))]
    private string? _holdingStatus;

    /// <summary>
    /// Why the profile picker is disabled, where it is. Absent in the ordinary case, because a control
    /// that is not greyed out has nothing to explain.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseProfile))]
    [NotifyPropertyChangedFor(nameof(HasProfileLock))]
    private string? _profileLock;

    /// <summary>The revision a past savegame held here pins the mod folder to. Null for everything else.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    private int? _pinnedRevision;

    [ObservableProperty]
    private string _driftNote = "Checking the mod folder...";

    /// <summary>
    /// Named separately from the count: an unlocked mod at the wrong version is untidy, a locked map
    /// at the wrong version is a damaged savegame waiting to happen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedWarning))]
    private string? _lockedWarning;

    /// <summary>
    /// The profile was deleted, or the user was removed from its repo. Said out loud, with the list
    /// still offering everything else - rather than reporting drift against something unreachable.
    /// </summary>
    [ObservableProperty]
    private bool _hasDanglingActiveProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasApplyStatus))]
    private string? _applyStatus;


    public bool HasLockedWarning => LockedWarning is not null;
    public bool HasApplyStatus => ApplyStatus is not null;
    public bool HasHoldingStatus => HoldingStatus is not null;
    public bool HasProfileLock => ProfileLock is not null;

    /// <summary>
    /// Whether the game may be pointed at a different profile at all. False while a savegame with
    /// a profile is checked out here: every switch in the app applies first, a held savegame refuses that
    /// apply, and a dropdown whose every other entry leads to a refusal is worse than one that says so
    /// and does not open. Savegames following no mod list leave it alone.
    /// </summary>
    public bool CanChooseProfile => ProfileLock is null;

    public ProfileActivationKind ActivationKind => SelectedProfile is InstanceProfileOptionViewModel option
        ? ProfileActivation.Describe(_game.ActiveProfile, option.Value)
        : ProfileActivationKind.Activate;

    public string ActivationLabel => ProfileActivation.Label(ActivationKind, PinnedRevision);


    protected override async Task InitAsync()
    {
        _fetchedOptions = await LoadProfileOptionsAsync(CancellationToken.None);
        _savegameNames = await LoadSavegameNamesAsync(CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        Profiles.Clear();

        foreach (var option in _fetchedOptions)
        {
            Profiles.Add(option);
        }

        SelectedProfile = _game.ActiveProfile is ActiveProfile active
            ? Profiles.FirstOrDefault(x => x.Value == active)
            : null;

        HasDanglingActiveProfile = _game.ActiveProfile is not null && SelectedProfile is null;

        RefreshHolding();
        RefreshDrift();
    }

    [RelayCommand(CanExecute = nameof(CanApply), IncludeCancelCommand = true)]
    private async Task Apply(CancellationToken cancellationToken)
    {
        if (SelectedProfile is not InstanceProfileOptionViewModel option)
        {
            return;
        }

        // A game is offered by every repo sharing its scope, so the profile picked here may well
        // belong to a repo other than the one navigated in through - and that repo's adapter is the
        // one that knows how to read its mod folder.
        if (FindRepo(option.Value.RepoId) is not Repo owner)
        {
            ApplyStatus = "That repo is no longer available on this machine.";

            return;
        }

        var kind = ActivationKind;

        IsApplying = true;
        ApplyStatus = kind is ProfileActivationKind.Apply ? "Re-applying..." : "Activating...";

        try
        {
            // The intent is recorded by the service, before any file moves and even where the folder
            // could not be touched: the game is still meant to follow this profile, and being left
            // drifted is what the notice is for.
            var outcome = await _applyService.ActivateAsync(
                owner,
                _game,
                option.Value.ProfileId,
                option.ProfileName,
                confirmPlan: kind is ProfileActivationKind.Activate,
                progress: null,
                cancellationToken);

            if (outcome.Activated)
            {
                HasDanglingActiveProfile = false;
            }

            ApplyStatus = outcome.Message;

            OnPropertyChanged(nameof(ActivationKind));
            OnPropertyChanged(nameof(ActivationLabel));

            RefreshDrift();

            await _driftMonitor.CheckAsync();
        }
        finally
        {
            IsApplying = false;
        }
    }

    private bool CanApply() => SelectedProfile is not null && IsApplying is false;

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
        ApplyCancelCommand.Execute(null);

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
    /// <b>Asked of <see cref="SavegameHoldRules"/> rather than worked out here.</b> The same three
    /// questions decide whether the sync engine refuses an apply, and a second copy of them on this
    /// page is one that eventually disagrees with the button it is greying out.
    /// </remarks>
    private void RefreshHolding()
    {
        var held = _savegameService.GetBindings(_game);
        var claiming = held.FirstOrDefault(x => x.ProfileId is not null);

        if (claiming.ProfileId is not Guid profileId)
        {
            HoldingStatus = null;
            ProfileLock = null;
            PinnedRevision = null;

            return;
        }

        // Quoted where it has a name and plain where it does not, so both readings are a sentence
        // rather than a name-shaped hole: the repo's list is a best-effort read, and a hold is a fact
        // about this machine whether or not it answered.
        var savegame = _savegameNames.GetValueOrDefault(claiming.SavegameId) is string named
            ? $"'{named}'"
            : "a savegame";

        var profile = _profileService.Profiles.FirstOrDefault(x => x.Id == profileId)?.Name ?? "its mod list";

        PinnedRevision = SavegameHoldRules.RequiredRevision(held, profileId);

        ProfileLock = $"This game is holding {savegame}, which follows '{profile}', so it stays on it. Check that savegame in to move somewhere else.";

        HoldingStatus = PinnedRevision is int pinned
            ? $"Holding {profile} rev {pinned} for {savegame}. Check {savegame} in to move this game forward."
            : $"Holding {savegame}, which follows {profile}.";
    }

    /// <summary>
    /// One line per folder this game reaches, since each of them matches its profile or does not on
    /// its own. The folder is named only where there is more than one to tell apart.
    /// </summary>
    private void RefreshDrift()
    {
        if (_game.Targets.Count == 0)
        {
            // No folder means no comparison, and there is no target to ask about one. The profile is
            // still worth a sentence, since setting one is what this page is for.
            DriftNote = (_game.ActiveProfile, HasDanglingActiveProfile) switch
            {
                (null, _) => "No profile is set on this game yet.",
                (_, true) => "The profile this game followed is gone. Pick another one.",
                _ => "No mod folder is configured, so nothing is known about what is installed."
            };
            LockedWarning = null;

            return;
        }

        var reports = _game.Targets
            .Select(target => (
                target.Key,
                Report: _driftService.Check(
                    new ModTargetRef(_game.Identity, target.Key),
                    _game.ActiveProfile,
                    target.ModFolder,
                    profileIsMissing: HasDanglingActiveProfile)))
            .ToList();

        DriftNote = string.Join(
            '\n',
            reports.Select(x => reports.Count > 1
                ? $"{x.Key}: {Describe(x.Report)}"
                : Describe(x.Report)));

        var locked = reports.SelectMany(x => x.Report.LockedDrift).DistinctBy(x => x.ModId).ToList();

        LockedWarning = locked.Count > 0
            ? $"{string.Join(", ", locked.Select(x => $"'{x.DisplayName}'"))} " +
              "are locked and no longer match what was applied. Hosting a savegame on them may damage that save."
            : null;
    }

    private static string Describe(DriftReport report)
    {
        return report.Status switch
        {
            DriftStatus.InSync => "The mod folder matches what was last applied here.",
            DriftStatus.Drifted =>
                $"{report.DifferenceCount} differences from what was last applied here. Updating mods from inside the game looks like this.",
            DriftStatus.NeverSynced => "This profile has not been applied to this game yet.",
            // Told apart because only one of them is something that went wrong: an apply that did not
            // land leaves the folder on the list it was on, and a repointed folder is a settings edit
            // somebody made a moment ago.
            DriftStatus.NotApplied => report.AppliedProfileName is string applied
                ? $"The mod folder is still on '{applied}'. This profile has not been applied here yet."
                : "The mod folder is still on the profile it was last applied to, not this one.",
            DriftStatus.FolderRepointed => "The mod folder has been pointed somewhere else, and nothing has been applied there yet.",
            DriftStatus.NoActiveProfile => "No profile is set on this game yet.",
            DriftStatus.DanglingProfile => "The profile this game followed is gone. Pick another one.",
            // Unknown, not drifted: warning about mods that may be perfectly fine is worse than
            // saying nothing.
            DriftStatus.FolderUnreachable => "The mod folder cannot be reached right now, so nothing is known about it.",
            _ => ""
        };
    }

    /// <summary>
    /// Every profile in every repo that shares this game's scope. One request per repo, and there
    /// are usually one or two.
    /// </summary>
    private async Task<IReadOnlyList<InstanceProfileOptionViewModel>> LoadProfileOptionsAsync(CancellationToken cancellationToken)
    {
        var repos = _repoRepository.Repos.Where(x => x.Scope == _game.Identity).ToList();
        var options = new List<InstanceProfileOptionViewModel>();

        foreach (var repo in repos)
        {
            try
            {
                var profiles = await _profilesClient.GetProfilesV1Async(repo.Id, cancellationToken);

                options.AddRange(profiles.Select(x =>
                    new InstanceProfileOptionViewModel(repo.Id, repo.Name, x.Id, x.Name, qualify: repos.Count > 1)));
            }
            catch (ApiException)
            {
                // One repo being unreadable is not a reason to offer none of the others.
            }
        }

        return options;
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

    private Repo? FindRepo(Guid repoId) => _repoRepository.Repos.FirstOrDefault(x => x.Id == repoId);


    public class Factory(IServiceProvider serviceProvider)
    {
        public GamePageViewModel Create(Repo repo, Game game)
            => ActivatorUtilities.CreateInstance<GamePageViewModel>(serviceProvider, repo, game);
    }
}
