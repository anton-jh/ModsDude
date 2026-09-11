using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The instance's own page: which profile it follows, whether its mod folder still matches, and the
/// one action that fixes it - with the settings and the name on the Manage sub-page below.
/// </summary>
/// <remarks>
/// <para>
/// Activation lives here because this is the end of it where the target is fixed and the profile is
/// chosen. The choice spans every repo sharing this instance's scope rather than the repo the user
/// navigated in through, since the instance is shared across all of them and holds one active profile
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
/// instance's end - see docs/10-savegame-profile-binding.md#instance-page.
/// </para>
/// </remarks>
public partial class InstancePageViewModel : PageViewModel, IDisposable
{
    private readonly LocalInstance _instance;
    private readonly RepoRepository _repoRepository;
    private readonly IProfilesClient _profilesClient;
    private readonly LocalInstanceRepository _localInstanceRepository;
    private readonly InstanceDriftService _driftService;
    private readonly InstanceDriftMonitor _driftMonitor;
    private readonly ProfileApplyService _applyService;
    private readonly ISavegameService _savegameService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly ISavegamesClient _savegamesClient;

    private IReadOnlyList<InstanceProfileOptionViewModel> _fetchedOptions = [];

    /// <summary>What the held savegames are called, so the status line can name one. Best effort.</summary>
    private IReadOnlyDictionary<Guid, string> _savegameNames = new Dictionary<Guid, string>();


    public InstancePageViewModel(
        Repo repo,
        LocalInstance instance,
        NavigationManager navigationManager,
        RepoRepository repoRepository,
        IProfilesClient profilesClient,
        LocalInstanceRepository localInstanceRepository,
        InstanceDriftService driftService,
        InstanceDriftMonitor driftMonitor,
        ProfileApplyService applyService,
        ISavegameService savegameService,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        ISavegamesClient savegamesClient,
        SyncPageViewModel.Factory syncPageViewModelFactory,
        InstanceSavegamesPageViewModel.Factory instanceSavegamesPageViewModelFactory,
        EditLocalInstancePageViewModel.Factory editLocalInstancePageViewModelFactory)
    {
        _instance = instance;
        _repoRepository = repoRepository;
        _profilesClient = profilesClient;
        _localInstanceRepository = localInstanceRepository;
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

        InstanceName = instance.Name;
        ModFolder = instance.ModFolder ?? "No mod folder configured";

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Sync", () => syncPageViewModelFactory.Create(repo, instance))
                .WithIcon(MenuIcons.Sync)
        ];

        // The local half of savegames: the slot list, and the one verb - publish - that is inherently
        // about a slot. Absent rather than closed where the game has no saves, for the same reason the
        // repo's Saves entry is.
        if (repo.Adapter.CanSupportSavegames)
        {
            MenuItems.Add(new MenuItemViewModel("Saves", () => instanceSavegamesPageViewModelFactory.Create(repo, instance))
                .WithIcon(MenuIcons.Saves));
        }

        MenuItems.Add(new MenuItemViewModel("Manage", () => editLocalInstancePageViewModelFactory.Create(repo, instance))
            .WithIcon(MenuIcons.Manage));

        NavManager.Selected = MenuItems.First();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }

    public ObservableCollection<InstanceProfileOptionViewModel> Profiles { get; } = [];

    public string InstanceName { get; }
    public string ModFolder { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private InstanceProfileOptionViewModel? _selectedProfile;

    /// <summary>
    /// What this instance is holding and what that demands of its mod folder, said in one Neutral
    /// line. Null - nearly always - where nothing with a profile is checked out here.
    /// </summary>
    /// <remarks>
    /// Neutral on purpose. Holding a past farm is a state somebody chose and is playing in, not a
    /// problem with the instance, so it reads like the mod-folder path underneath it rather than like
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
    private string _driftStatus = "Checking the mod folder...";

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
    /// Whether the instance may be pointed at a different profile at all. False while a savegame with
    /// a profile is checked out here: every switch in the app applies first, a held farm refuses that
    /// apply, and a dropdown whose every other entry leads to a refusal is worse than one that says so
    /// and does not open. Savegames following no mod list leave it alone.
    /// </summary>
    public bool CanChooseProfile => ProfileLock is null;

    public InstanceActivationKind ActivationKind => SelectedProfile is InstanceProfileOptionViewModel option
        ? InstanceActivation.Describe(_instance.ActiveProfile, option.Value)
        : InstanceActivationKind.Activate;

    public string ActivationLabel => InstanceActivation.Label(ActivationKind, PinnedRevision);


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

        SelectedProfile = _instance.ActiveProfile is ActiveProfile active
            ? Profiles.FirstOrDefault(x => x.Value == active)
            : null;

        HasDanglingActiveProfile = _instance.ActiveProfile is not null && SelectedProfile is null;

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

        // An instance is offered by every repo sharing its scope, so the profile picked here may well
        // belong to a repo other than the one navigated in through - and that repo's adapter is the
        // one that knows how to read its mod folder.
        if (FindRepo(option.Value.RepoId) is not Repo owner)
        {
            ApplyStatus = "That repo is no longer available on this machine.";

            return;
        }

        var kind = ActivationKind;

        IsApplying = true;
        ApplyStatus = kind is InstanceActivationKind.Reapply ? "Re-applying..." : "Activating...";

        try
        {
            var outcome = await _applyService.ApplyAsync(
                owner,
                _instance,
                option.Value.ProfileId,
                option.ProfileName,
                confirmPlan: kind is InstanceActivationKind.Activate,
                progress: null,
                cancellationToken);

            // The intent is recorded even where the folder could not be touched: the instance is still
            // meant to follow this profile, and being left drifted is what the notice is for.
            if (outcome.RecordsIntent)
            {
                _localInstanceRepository.SetActiveProfile(_instance, option.Value);
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
        var held = _savegameService.GetBindings(_instance);
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

        ProfileLock = $"This instance is holding {savegame}, which follows '{profile}', so it stays on it. Check that savegame in to move somewhere else.";

        HoldingStatus = PinnedRevision is int pinned
            ? $"Holding {profile} rev {pinned} for {savegame}. Check {savegame} in to move this instance forward."
            : $"Holding {savegame}, which follows {profile}.";
    }

    private void RefreshDrift()
    {
        var report = _driftService.Check(
            _instance.Id,
            _instance.ActiveProfile,
            _instance.ModFolder,
            profileIsMissing: HasDanglingActiveProfile);

        DriftStatus = report.Status switch
        {
            InstanceDriftStatus.InSync => "The mod folder matches what was last applied here.",
            InstanceDriftStatus.Drifted =>
                $"{report.DifferenceCount} differences from what was last applied here. Updating mods from inside the game looks like this.",
            InstanceDriftStatus.NeverSynced => "This profile has not been applied to this instance yet.",
            InstanceDriftStatus.NoActiveProfile => "No profile is set on this instance yet.",
            InstanceDriftStatus.DanglingProfile => "The profile this instance followed is gone. Pick another one.",
            // Unknown, not drifted: warning about mods that may be perfectly fine is worse than
            // saying nothing.
            InstanceDriftStatus.FolderUnreachable => "The mod folder cannot be reached right now, so nothing is known about it.",
            _ => ""
        };

        LockedWarning = report.LockedDrift.Count > 0
            ? $"{string.Join(", ", report.LockedDrift.Select(x => $"'{x.DisplayName}'"))} " +
              "are locked and no longer match what was applied. Hosting a savegame on them may damage that save."
            : null;
    }

    /// <summary>
    /// Every profile in every repo that shares this instance's scope. One request per repo, and there
    /// are usually one or two.
    /// </summary>
    private async Task<IReadOnlyList<InstanceProfileOptionViewModel>> LoadProfileOptionsAsync(CancellationToken cancellationToken)
    {
        var repos = _repoRepository.Repos.Where(x => x.Scope == _instance.Scope).ToList();
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
    /// What the savegames this instance holds are called, for the one line that names one.
    /// </summary>
    /// <remarks>
    /// Best effort and absorbed on failure: a held binding is a fact about this machine and stays true
    /// whether or not the repo answers, so a name that could not be read costs a word in a sentence
    /// and nothing else. Archived savegames are read too - archiving does not release a hold.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadSavegameNamesAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();

        foreach (var repoId in _savegameService.GetBindings(_instance).Select(x => x.RepoId).Distinct())
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
        public InstancePageViewModel Create(Repo repo, LocalInstance instance)
            => ActivatorUtilities.CreateInstance<InstancePageViewModel>(serviceProvider, repo, instance);
    }
}
