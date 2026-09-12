using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The profile's shell, and the profile-side half of activation.
/// </summary>
/// <remarks>
/// <para>
/// The activation control sits here rather than on Overview so that it is present on every sub-page.
/// From this end the profile is fixed and the game is chosen, which is why there is a dropdown at
/// all - and none when the repo offers a single game, which is the common case for most games.
/// </para>
/// <para>
/// It is <b>labelled for what it will do</b>: a game already on this profile is being re-applied,
/// one on another profile or none is being moved, and moving it uninstalls whatever the previous
/// profile put in the folder. See docs/07-mod-sync-design.md#activating-a-profile-on-an-game.
/// </para>
/// <para>
/// <b>And refused before the click where a held savegame forbids it.</b> This is the game page's
/// disabled profile dropdown seen from the other end - the same switch, the same rule - and the apply
/// table refuses it either way. A control that offers the move and then reports a refusal is the
/// thing slice 4 set out to remove, so it is asked here too.
/// </para>
/// </remarks>
public partial class ProfilePageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly GameRepository _gameRepository;
    private readonly ProfileApplyService _applyService;
    private readonly IHeldSavegames _heldSavegames;
    private readonly DriftMonitor _driftMonitor;
    private readonly MenuItemViewModel _modsMenuItem;
    private readonly MenuItemViewModel _historyMenuItem;

    private ProfileModsEditorPageViewModel? _openModsEditor;

    /// <summary>Set by a drift deep link, consumed by the next page the Mods entry builds.</summary>
    private GameIdentity? _scanGameOnce;

    /// <summary>
    /// Which revision a deep link into the history asked for. Same one-shot shape as
    /// <see cref="_scanGameOnce"/>, and for the same reason: it describes an arrival, not a
    /// standing preference.
    /// </summary>
    private int? _selectRevisionOnce;


    public ProfilePageViewModel(
        Repo repo,
        ProfileDto profile,
        NavigationManager navigationManager,
        GameRepository gameRepository,
        ProfileApplyService applyService,
        IHeldSavegames heldSavegames,
        DriftMonitor driftMonitor,
        ProfileOverviewPageViewModel.Factory profileOverviewPageViewModelFactory,
        EditProfilePageViewModel.Factory editProfilePageViewModelFactory,
        ProfileModsEditorPageViewModel.Factory profileModsEditorPageViewModelFactory,
        ProfileModsPageViewModel.Factory profileModsPageViewModelFactory,
        ProfileHistoryPageViewModel.Factory profileHistoryPageViewModelFactory)
    {
        _repo = repo;
        _profile = profile;
        _gameRepository = gameRepository;
        _applyService = applyService;
        _heldSavegames = heldSavegames;
        _driftMonitor = driftMonitor;

        // One entry, two pages. Editing a profile's mod list needs Member, but *seeing* it needs
        // only Guest - and a guest is precisely the person who syncs this profile without curating
        // it, so "what is in it?" is their question to ask. Closing the entry would have answered it
        // with silence; the read-only page answers it.
        var canEditMods = repo.MembershipLevel >= RepoMembershipLevel.Member;

        _modsMenuItem = new MenuItemViewModel("Mods", () =>
        {
            if (canEditMods is false)
            {
                return profileModsPageViewModelFactory.Create(repo, profile);
            }

            var scanGame = _scanGameOnce;
            _scanGameOnce = null;

            return profileModsEditorPageViewModelFactory.Create(repo, profile, scanGame);
        }).WithIcon(MenuIcons.Mods);

        // Open to a guest, like the read-only mod list and for the same reason: somebody who syncs
        // this profile without curating it is exactly the person who wants to know what changed under
        // them. Restoring and branching are what a Member level buys, and the page hides those
        // controls rather than closing the entry.
        _historyMenuItem = new MenuItemViewModel("History", () =>
        {
            var selectRevision = _selectRevisionOnce;
            _selectRevisionOnce = null;

            return profileHistoryPageViewModelFactory.Create(repo, profile, selectRevision);
        }).WithIcon(MenuIcons.History);

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Overview", () => profileOverviewPageViewModelFactory.Create(repo, profile))
                .WithIcon(MenuIcons.Overview),
            _modsMenuItem,
            _historyMenuItem,
            new MenuItemViewModel("Manage", () => editProfilePageViewModelFactory.Create(repo, profile))
                .WithIcon(MenuIcons.Manage)
                .RestrictIf(canEditMods is false, "Guests cannot rename or delete a profile. Ask an admin for a higher membership level.")
        ];

        Games = [];

        NavManager.Selected = MenuItems.First();
        NavManager.PropertyChanged += OnNavigationChanged;

        _repo.Games.CollectionChanged += OnGamesChanged;

        RefreshInstances();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }

    /// <summary>The games this repo offers, which are compatible with it by construction.</summary>
    public ObservableCollection<Game> Games { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private Game? _selectedGame;

    /// <summary>
    /// Why the selected game cannot be put on this profile, where it cannot. Null - nearly
    /// always - where nothing is in the way.
    /// </summary>
    /// <remarks>
    /// Asked of <see cref="IHeldSavegames.DecideApply"/>, which is the rule the sync engine refuses
    /// with. A second copy of it here is one that eventually disagrees with the button it is greying
    /// out.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHoldRefusal))]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private string? _holdRefusal;

    public bool HasHoldRefusal => HoldRefusal is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameChoice))]
    [NotifyPropertyChangedFor(nameof(HasActivation))]
    private int _instanceCount;

    /// <summary>
    /// The mod list editor's own <em>Save and apply</em> is the way to apply pending edits. This
    /// control would otherwise silently apply the last-saved profile behind them.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private bool _blockedByUnsavedChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private bool _isApplying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivationStatus))]
    private string? _activationStatus;


    public bool HasActivation => InstanceCount > 0;

    /// <summary>With one game there is nothing to pick, so the dropdown does not appear at all.</summary>
    public bool HasGameChoice => InstanceCount > 1;

    public bool HasActivationStatus => ActivationStatus is not null;

    public InstanceActivationKind ActivationKind => InstanceActivation.Describe(
        SelectedGame?.ActiveProfile,
        new ActiveProfile(_repo.Id, _profile.Id));

    public string ActivationLabel => InstanceActivation.Label(ActivationKind);

    public string ActivationDescription
    {
        get
        {
            if (BlockedByUnsavedChanges)
            {
                return "The mod list has unsaved changes. Use 'Save and apply' there instead - this would apply the last saved version behind them.";
            }

            if (HoldRefusal is string refused)
            {
                return refused;
            }

            if (SelectedGame is not Game game)
            {
                return "";
            }

            return ActivationKind is InstanceActivationKind.Reapply
                ? $"'{game.Name}' already follows this profile. Applying it again makes the mod folder match."
                : $"'{game.Name}' will start following this profile. Whatever its current profile put in the mod folder is taken back out.";
        }
    }


    [RelayCommand(CanExecute = nameof(CanActivate), IncludeCancelCommand = true)]
    private async Task Activate(CancellationToken cancellationToken)
    {
        if (SelectedGame is not Game game)
        {
            return;
        }

        var kind = ActivationKind;
        var target = new ActiveProfile(_repo.Id, _profile.Id);

        IsApplying = true;
        ActivationStatus = kind is InstanceActivationKind.Reapply ? "Re-applying..." : "Activating...";

        try
        {
            var outcome = await _applyService.ApplyAsync(
                _repo,
                game,
                _profile.Id,
                _profile.Name,
                // Moving a game onto a different profile takes the previous one's mods back out,
                // so the plan is shown first. A re-apply has nothing extra to disclose.
                confirmPlan: kind is InstanceActivationKind.Activate,
                progress: null,
                cancellationToken);

            // The intent is recorded whatever the folder ended up doing: a game that could not be
            // reached is still meant to follow this profile, and the drift notice covers the rest.
            if (outcome.RecordsIntent)
            {
                _gameRepository.SetActiveProfile(game, target);
            }

            ActivationStatus = outcome.Message;

            OnPropertyChanged(nameof(ActivationKind));
            OnPropertyChanged(nameof(ActivationLabel));
            OnPropertyChanged(nameof(ActivationDescription));

            await _driftMonitor.CheckAsync();
        }
        finally
        {
            IsApplying = false;
        }
    }

    private bool CanActivate()
        => SelectedGame is not null
        && IsApplying is false
        && BlockedByUnsavedChanges is false
        && HasHoldRefusal is false;

    /// <summary>
    /// Whether a savegame checked out on the selected game forbids putting it on this profile.
    /// </summary>
    /// <remarks>
    /// Only the outright refusal is a block. A <em>past</em> savegame of this very profile pins the folder
    /// to its own revision without forbidding the apply - re-applying that revision is what repairs
    /// folder drift under it - and <see cref="ProfileApplyService.ApplyAsync"/> installs the pinned one
    /// on its own, so the button keeps working and its message names the number.
    /// </remarks>
    private void RefreshHoldRefusal()
    {
        if (SelectedGame is not Game game)
        {
            HoldRefusal = null;

            return;
        }

        HoldRefusal = _heldSavegames.DecideApply(game.Identity, _profile.Id, revision: null) is { IsAllowed: false }
            ? $"'{game.Name}' is holding a savegame that follows another mod list, so it cannot be moved to this profile. Check that savegame in first."
            : null;
    }


    /// <summary>Selects the Mods sub-page, for a deep link from the drift notice.</summary>
    /// <param name="scanGame">
    /// A game whose mod folder the editor should open already scanning. Sources are off by
    /// default because opening a page must not read a disk - but arriving here from a drift notice
    /// <em>is</em> the user asking about that folder's contents, so the one it is about is on.
    /// </param>
    public bool TrySelectMods(GameIdentity? scanGame = null)
    {
        // Read and cleared by the menu item's factory, so it applies to the page this call opens and
        // not to the next one somebody reaches through the sidebar.
        _scanGameOnce = scanGame;

        if (ReferenceEquals(NavManager.Selected, _modsMenuItem) is false)
        {
            NavManager.Selected = _modsMenuItem;
        }
        else if (scanGame is not null)
        {
            // Already open, so selecting it again constructs nothing and the factory never runs.
            // Enable the folder on the page the user is looking at instead.
            (NavManager.CurrentPage as ProfileModsEditorPageViewModel)?.ScanGame(scanGame.Value);
            _scanGameOnce = null;
        }

        return ReferenceEquals(NavManager.Selected, _modsMenuItem);
    }

    /// <summary>
    /// Selects the History sub-page, for a deep link from a savegame - whose versions each name the
    /// revision they were played on, and whose "what changed under this save" is exactly the question
    /// that page already answers.
    /// </summary>
    public bool TrySelectHistory(int? selectRevision = null)
    {
        _selectRevisionOnce = selectRevision;

        if (ReferenceEquals(NavManager.Selected, _historyMenuItem) is false)
        {
            NavManager.Selected = _historyMenuItem;
        }

        var selected = ReferenceEquals(NavManager.Selected, _historyMenuItem);

        if (selected is false)
        {
            // Refused, so nothing read the value and it must not be waiting for whoever opens the
            // history next.
            _selectRevisionOnce = null;
        }

        return selected;
    }

    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on - the same reason <see cref="RepoModsPageViewModel"/>
    /// is disposable.
    /// </summary>
    public void Dispose()
    {
        DetachModsEditor();

        NavManager.PropertyChanged -= OnNavigationChanged;
        _repo.Games.CollectionChanged -= OnGamesChanged;

        NavManager.Dispose();
    }


    /// <summary>
    /// Re-asks the hold question for whichever game is selected now.
    /// </summary>
    /// <remarks>
    /// Selection is the only thing that can change the answer while this page is up: checking a
    /// savegame in happens on a repo's Saves list or a game's own, and reaching either means
    /// leaving this page - which rebuilds it. Subscribing to the binding store as well would be
    /// covering a window that does not exist.
    /// </remarks>
    partial void OnSelectedGameChanged(Game? value)
    {
        RefreshHoldRefusal();
    }

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshInstances();
    }

    private void RefreshInstances()
    {
        var previous = SelectedGame;

        Games.Clear();

        foreach (var game in _repo.Games)
        {
            Games.Add(game);
        }

        InstanceCount = Games.Count;

        // Prefer one already on this profile: with several games the likeliest intent is
        // re-applying, and that is also the one the label has to get right on first sight.
        SelectedGame = previous is not null && Games.Contains(previous) ? previous : Games
            .FirstOrDefault(x => x.ActiveProfile == new ActiveProfile(_repo.Id, _profile.Id))
            ?? Games.FirstOrDefault();

        OnPropertyChanged(nameof(ActivationKind));
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavigationManager.CurrentPage))
        {
            return;
        }

        DetachModsEditor();

        if (NavManager.CurrentPage is ProfileModsEditorPageViewModel editor)
        {
            _openModsEditor = editor;
            _openModsEditor.PropertyChanged += OnModsEditorChanged;

            BlockedByUnsavedChanges = editor.HasUnsavedChanges;
        }
    }

    private void OnModsEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileModsEditorPageViewModel.HasUnsavedChanges) && _openModsEditor is not null)
        {
            BlockedByUnsavedChanges = _openModsEditor.HasUnsavedChanges;
        }
    }

    private void DetachModsEditor()
    {
        if (_openModsEditor is not null)
        {
            _openModsEditor.PropertyChanged -= OnModsEditorChanged;
            _openModsEditor = null;
        }

        BlockedByUnsavedChanges = false;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfilePageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfilePageViewModel>(serviceProvider, repo, profile);
    }
}
