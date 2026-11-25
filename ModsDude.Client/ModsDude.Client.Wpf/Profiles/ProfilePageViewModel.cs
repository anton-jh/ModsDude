using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Concurrency;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Mods;
using ModsDude.Client.Wpf.Profiles.Editor;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// The profile's shell: its tabs, and the activation beside them.
/// </summary>
/// <remarks>
/// <para>
/// Activation lives here rather than on a tab so it is the same from every tab, and it takes no
/// target: a repo is about one game and a machine configures that game once, so there is nothing to
/// pick. A game reaching three folders applies to all three.
/// </para>
/// <para>
/// It is <b>labelled for what it will do</b>: a game already on this profile is being re-applied,
/// one on another profile or none is being moved, and moving it uninstalls whatever the previous
/// profile put in the folder.
/// </para>
/// <para>
/// <b>And refused before the click where a held savegame forbids it.</b> The apply table refuses it
/// anyway; asking here makes the refusal arrive before the click rather than after it.
/// </para>
/// </remarks>
public partial class ProfilePageViewModel : PageViewModel, INavigationHost, IDisposable
{
    private readonly Repo _repo;
    private readonly Profile _profile;
    private readonly IProfileApplyService _applyService;
    private readonly IHeldSavegames _heldSavegames;
    private readonly IGoneRepos _goneRepos;
    private readonly ISavegameBindingStore _bindings;
    private readonly IDriftMonitor _driftMonitor;
    private readonly IProfileSyncStatusService _syncStatus;
    private readonly IResourceLeases _leases;
    private readonly IToastService _toasts;
    private readonly IRetainedPages _retainedPages;
    private readonly ProfileModsEditorKey _modsEditorKey;
    private readonly MenuItemViewModel _modsMenuItem;
    private readonly MenuItemViewModel _historyMenuItem;

    /// <summary>Set by a drift deep link, consumed by the next page the Mods entry builds.</summary>
    private PinnedModFilter? _reviewOnce;

    /// <summary>
    /// Where a deep link into the history asked to arrive. Same one-shot shape as
    /// <see cref="_reviewOnce"/>, and for the same reason: it describes an arrival, not a
    /// standing preference.
    /// </summary>
    private HistoryArrival? _historyArrivalOnce;


    public ProfilePageViewModel(
        Repo repo,
        Profile profile,
        NavigationManager navigationManager,
        IProfileApplyService applyService,
        IHeldSavegames heldSavegames,
        IGoneRepos goneRepos,
        ISavegameBindingStore bindings,
        IDriftMonitor driftMonitor,
        IProfileSyncStatusService syncStatus,
        IResourceLeases leases,
        IToastService toasts,
        IRetainedPages retainedPages,
        ProfileOverviewPageViewModel.Factory profileOverviewPageViewModelFactory,
        ProfileModsEditorPageViewModel.Factory profileModsEditorPageViewModelFactory,
        ProfileModsPageViewModel.Factory profileModsPageViewModelFactory,
        ProfileHistoryPageViewModel.Factory profileHistoryPageViewModelFactory)
    {
        _repo = repo;
        _profile = profile;
        _applyService = applyService;
        _heldSavegames = heldSavegames;
        _goneRepos = goneRepos;
        _bindings = bindings;
        _driftMonitor = driftMonitor;
        _syncStatus = syncStatus;
        _leases = leases;
        _toasts = toasts;
        _retainedPages = retainedPages;
        _modsEditorKey = new ProfileModsEditorKey(repo.Id, profile.Id);

        _syncStatus.Changed += OnSyncStatusChanged;
        _bindings.BindingsChanged += OnBindingsChanged;

        // The activation button is greyed by a claim anything in the app can take - the drift notice
        // most of all, which belongs to no page - so the only way it can be right is to re-ask when
        // that changes.
        _leases.Changed += OnLeasesChanged;

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

            var review = _reviewOnce;
            _reviewOnce = null;

            return profileModsEditorPageViewModelFactory.Create(repo, profile, review);
        }).WithIcon(MenuIcons.Mods);

        // Open to a guest, like the read-only mod list and for the same reason: somebody who syncs
        // this profile without curating it is exactly the person who wants to know what changed under
        // them. Restoring and branching are what a Member level buys, and the page hides those
        // controls rather than closing the entry.
        _historyMenuItem = new MenuItemViewModel("History", () =>
        {
            var arrival = _historyArrivalOnce;
            _historyArrivalOnce = null;

            return profileHistoryPageViewModelFactory.Create(repo, profile, arrival);
        }).WithIcon(MenuIcons.History);

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Overview", () => profileOverviewPageViewModelFactory.Create(repo, profile))
                .WithIcon(MenuIcons.Overview),
            _modsMenuItem,
            _historyMenuItem
        ];

        NavManager.Selected = MenuItems.First();

        _retainedPages.Changed += OnRetainedPagesChanged;
        OnRetainedPagesChanged();

        _repo.Games.CollectionChanged += OnGamesChanged;

        RefreshConnectedGame();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }


    /// <summary>
    /// This machine's installation of the game this repo is about, or null where none is connected -
    /// which is the only state in which the control is not drawn at all.
    /// </summary>
    /// <remarks>
    /// Named for what it is rather than <c>Game</c>, which would put a member and the type of that
    /// member's value under one name in a file that uses both.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivation))]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyPropertyChangedFor(nameof(HasDeactivation))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateAndClearCommand))]
    private Game? _connectedGame;

    /// <summary>
    /// Why the game cannot be put on this profile, where it cannot. Null - nearly always - where
    /// nothing is in the way.
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

    /// <summary>
    /// The mod list editor's own <em>Save and apply</em> is the way to apply pending edits. This
    /// control would otherwise silently apply the last-saved profile behind them.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private bool _blockedByUnsavedChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyPropertyChangedFor(nameof(DeactivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateAndClearCommand))]
    private bool _isApplying;

    /// <summary>
    /// Why the game cannot be taken off its profile, where it cannot: a savegame with a profile is
    /// checked out. The other half of <see cref="HoldRefusal"/>, from
    /// <see cref="IHeldSavegames.FindProfileHold"/> and for the same reason - one copy of the rule.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeactivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateAndClearCommand))]
    private string? _deactivationRefusal;

    /// <summary>
    /// Whether there is anything to activate on. Nothing at all is drawn where this repo's game is
    /// not connected on this machine - a button whose only outcome is a sentence about connecting one
    /// is worse than the sidebar entry that already says so.
    /// </summary>
    public bool HasActivation => ConnectedGame is not null;

    public ProfileActivationKind ActivationKind => ProfileActivation.Describe(
        ConnectedGame?.ActiveProfile,
        new ActiveProfile(_repo.Id, _profile.Id),
        ConnectedGame?.PinnedRevision);

    /// <summary>Whether the game follows this profile at all, head or pinned.</summary>
    private bool IsFollowed => ConnectedGame?.ActiveProfile == new ActiveProfile(_repo.Id, _profile.Id);

    public string ActivationLabel => ProfileActivation.Label(ActivationKind);

    /// <summary>
    /// Where this profile stands against the game that follows it - none of the four unless it is the
    /// profile the game follows.
    /// </summary>
    public ProfileSyncState SyncState => _syncStatus.StateOf(_repo, _profile.Id);

    public bool HasSyncState => SyncState is not ProfileSyncState.None;

    public string SyncLabel => ProfileSyncStatusService.Describe(SyncState);

    /// <summary>
    /// Why the button cannot be pressed, where it cannot: unsaved edits, a held savegame, or an apply
    /// already running. Null - nearly always - where nothing is in the way.
    /// </summary>
    /// <remarks>Only ever said in the button's tooltip; the header stays quiet.</remarks>
    private string? ActivationBlockReason
    {
        get
        {
            if (BlockedByUnsavedChanges)
            {
                return "Unsaved changes. Save them on the Mods page first.";
            }

            if (HoldRefusal is string refused)
            {
                return refused;
            }

            if (ConnectedGame is Game game && IsApplying is false && _applyService.IsBusy(_repo, game))
            {
                return _applyService.Busy(_repo, game);
            }

            return null;
        }
    }

    public string? ActivationDescription
    {
        get
        {
            if (ActivationBlockReason is string reason)
            {
                return reason;
            }

            if (ConnectedGame is Game game && IsFollowed && game.PinnedRevision is int pinned)
            {
                return $"On revision {pinned}. Activating moves it to the latest.";
            }

            return null;
        }
    }


    [RelayCommand(CanExecute = nameof(CanActivate), IncludeCancelCommand = true)]
    private async Task Activate(CancellationToken cancellationToken)
    {
        if (ConnectedGame is not Game game)
        {
            return;
        }

        var kind = ActivationKind;

        // No line saying it has started: the strip along the top already names the work while it runs.
        IsApplying = true;

        try
        {
            // Always the activation verb, even where the game already follows this profile: that is
            // a decision being made again rather than a different act, and the service is what
            // notices there is nothing new to record. Whatever the folders end up doing, the intent
            // stands - a game that could not be reached is still meant to follow this profile, and
            // the drift notice covers the rest.
            var outcome = await _applyService.ActivateAsync(
                _repo,
                game,
                _profile.Id,
                _profile.Name,
                // Moving a game onto a different profile takes the previous one's mods back out,
                // so the plan is shown first. A re-apply has nothing extra to disclose.
                confirmPlan: kind is ProfileActivationKind.Activate,
                progress: null,
                cancellationToken);

            _toasts.Show(outcome.Message, outcome.ToastSeverity);

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

    /// <summary>
    /// Whether the game follows this profile, which is the only state in which deactivating it is
    /// offered from here. Another profile's page has nothing to say about a game it does not follow.
    /// </summary>
    public bool HasDeactivation => IsFollowed;

    /// <summary>
    /// The caret's tooltip, or why neither way of deactivating can be used right now. The two menu items
    /// say the difference between them themselves.
    /// </summary>
    public string DeactivationDescription
    {
        get
        {
            if (DeactivationRefusal is string refused)
            {
                return refused;
            }

            if (ConnectedGame is Game game && IsApplying is false && _applyService.IsBusy(_repo, game))
            {
                return _applyService.Busy(_repo, game);
            }

            return "Deactivate";
        }
    }

    /// <summary>
    /// Stop following this profile and leave the mod folders exactly as they are, for managing the mods by
    /// hand for a while.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeactivate))]
    private Task Deactivate(CancellationToken cancellationToken)
        => DeactivateAsync(clearMods: false, cancellationToken);

    /// <summary>Stop following this profile, and take every mod out of the folders too.</summary>
    [RelayCommand(CanExecute = nameof(CanDeactivate))]
    private Task DeactivateAndClear(CancellationToken cancellationToken)
        => DeactivateAsync(clearMods: true, cancellationToken);

    private async Task DeactivateAsync(bool clearMods, CancellationToken cancellationToken)
    {
        if (ConnectedGame is not Game game)
        {
            return;
        }

        IsApplying = true;

        try
        {
            var outcome = await _applyService.DeactivateAsync(_repo, game, clearMods, progress: null, cancellationToken);

            _toasts.Show(outcome.Message, outcome.ToastSeverity);

            OnPropertyChanged(nameof(ActivationKind));
            OnPropertyChanged(nameof(ActivationLabel));
            OnPropertyChanged(nameof(ActivationDescription));
            OnPropertyChanged(nameof(HasDeactivation));

            await _driftMonitor.CheckAsync();
        }
        finally
        {
            IsApplying = false;
        }
    }

    private bool CanDeactivate()
        => ConnectedGame is not null
        && IsApplying is false
        && IsGameBusy is false
        && DeactivationRefusal is null;

    /// <summary>
    /// <see cref="IsApplying"/> covers this page's own click. The lease covers everything else, which
    /// is most of it: the drift notice's one-click re-apply, the mod list editor's save, the savegame
    /// list's two routes - and this page's own click after somebody navigated away and back, which
    /// builds a fresh page whose flag is false while the apply it started is still moving files.
    /// </summary>
    private bool CanActivate()
        => ConnectedGame is not null
        && IsApplying is false
        && IsGameBusy is false
        && BlockedByUnsavedChanges is false
        && HasHoldRefusal is false;

    /// <summary>Whether something is already applying to one of this game's folders.</summary>
    private bool IsGameBusy => ConnectedGame is Game game && _applyService.IsBusy(_repo, game);

    /// <summary>
    /// Something somewhere claimed or released a resource, so the button and the sentence under it
    /// are now possibly wrong.
    /// </summary>
    /// <remarks>
    /// Marshalled, because a claim is released by whichever thread finished the work - see
    /// <see cref="IResourceLeases.Changed"/>.
    /// </remarks>
    private void OnLeasesChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ActivateCommand.NotifyCanExecuteChanged();
            DeactivateCommand.NotifyCanExecuteChanged();
            DeactivateAndClearCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(ActivationDescription));
            OnPropertyChanged(nameof(DeactivationDescription));
        });
    }

    /// <summary>
    /// The pill and the sentence beside it, re-asked: which profile the game follows and whether its
    /// folders match both change without anything on this page being touched.
    /// </summary>
    private void OnSyncStatusChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SyncState));
        OnPropertyChanged(nameof(HasSyncState));
        OnPropertyChanged(nameof(SyncLabel));
        OnPropertyChanged(nameof(ActivationKind));
        OnPropertyChanged(nameof(ActivationLabel));
        OnPropertyChanged(nameof(ActivationDescription));
        OnPropertyChanged(nameof(HasDeactivation));
        OnPropertyChanged(nameof(DeactivationDescription));
    }

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
        if (ConnectedGame is not Game game)
        {
            HoldRefusal = null;
            DeactivationRefusal = null;

            return;
        }

        DeactivationRefusal = _heldSavegames.FindProfileHold(game.Identity) is SavegameCheckoutBinding held
            ? $"A savegame is checked out on '{game.Name}'. "
                + SavegameRefusals.FreeHeld("that savegame", _goneRepos.IsGone(held.RepoId))
            : null;

        HoldRefusal = _heldSavegames.DecideApply(game.Identity, _profile.Id, revision: null) is { IsAllowed: false } refusal
            ? $"A savegame is checked out on '{game.Name}'. "
                + SavegameRefusals.FreeHeld("that savegame", _goneRepos.IsGone(refusal.RepoId))
            : null;
    }


    /// <summary>Selects the Mods sub-page, for a deep link from the drift notice.</summary>
    /// <param name="review">
    /// Opens the editor reviewing the game's mod folders with this filter. Sources are off by default
    /// because opening a page must not read a disk, but arriving from a drift notice <em>is</em> the
    /// user asking about those folders.
    /// </param>
    public bool TrySelectMods(PinnedModFilter? review = null)
    {
        // Read and cleared by the menu item's factory, so it applies to the page this call opens and
        // not to the next one somebody reaches through the tabs.
        _reviewOnce = review;

        if (ReferenceEquals(NavManager.Current, _modsMenuItem) is false)
        {
            NavManager.Selected = _modsMenuItem;
        }
        else if (review is PinnedModFilter filter)
        {
            // Already open, so selecting it again constructs nothing and the factory never runs.
            (NavManager.CurrentPage as ProfileModsEditorPageViewModel)?.Review(filter);
            _reviewOnce = null;
        }

        return ReferenceEquals(NavManager.Current, _modsMenuItem);
    }

    /// <summary>
    /// Selects the History sub-page, for a deep link from a savegame, a mod or a drift notice.
    /// </summary>
    public bool TrySelectHistory(HistoryArrival arrival)
    {
        // Read and cleared by the menu item's factory, so it applies to the page this call opens and
        // not to the next one somebody reaches through the tabs.
        _historyArrivalOnce = arrival;

        if (ReferenceEquals(NavManager.Current, _historyMenuItem) is false)
        {
            NavManager.Selected = _historyMenuItem;
        }

        var selected = ReferenceEquals(NavManager.Current, _historyMenuItem);

        // Unread where the page was already open, so the factory never ran.
        if (selected && _historyArrivalOnce is HistoryArrival unread)
        {
            (NavManager.CurrentPage as ProfileHistoryPageViewModel)?.Arrive(unread);
        }

        // Read, handed to the open page, or refused: either way it must not be waiting for whoever
        // opens the history next.
        _historyArrivalOnce = null;

        return selected;
    }

    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on - the same reason <see cref="RepoModsPageViewModel"/>
    /// is disposable.
    /// </summary>
    public void Dispose()
    {
        _retainedPages.Changed -= OnRetainedPagesChanged;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _leases.Changed -= OnLeasesChanged;
        _syncStatus.Changed -= OnSyncStatusChanged;
        _bindings.BindingsChanged -= OnBindingsChanged;

        NavManager.Dispose();
    }


    /// <summary>Re-asks the hold question for the game the control now acts on.</summary>
    partial void OnConnectedGameChanged(Game? value)
    {
        RefreshHoldRefusal();
    }

    /// <summary>
    /// A hold taken or let go while this page is up - stopping tracking from the notice column is one.
    /// </summary>
    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshHoldRefusal);
    }

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshConnectedGame();
    }

    /// <summary>
    /// Which game this control acts on, which is not a choice: a repo is about one game and a
    /// machine configures that game once.
    /// </summary>
    private void RefreshConnectedGame()
    {
        ConnectedGame = _repo.Games.FirstOrDefault();

        OnPropertyChanged(nameof(ActivationKind));
    }

    /// <summary>The mod list editor holds work on screen or kept hidden, which blocks activating and marks its entry.</summary>
    private void OnRetainedPagesChanged()
    {
        BlockedByUnsavedChanges = _retainedPages.HoldsWork(_modsEditorKey);
        _modsMenuItem.Attention = _retainedPages.IsKept(_modsEditorKey) ? "Unsaved changes" : null;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfilePageViewModel Create(Repo repo, Profile profile)
            => ActivatorUtilities.CreateInstance<ProfilePageViewModel>(serviceProvider, repo, profile);
    }
}
