using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

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

    private IReadOnlyList<InstanceProfileOptionViewModel> _fetchedOptions = [];


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

    public InstanceActivationKind ActivationKind => SelectedProfile is InstanceProfileOptionViewModel option
        ? InstanceActivation.Describe(_instance.ActiveProfile, option.Value)
        : InstanceActivationKind.Activate;

    public string ActivationLabel => InstanceActivation.Label(ActivationKind);


    protected override async Task InitAsync()
    {
        _fetchedOptions = await LoadProfileOptionsAsync(CancellationToken.None);
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
            if (outcome.Status is not ProfileApplyStatus.Declined)
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

        RefreshDrift();
    }

    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on.
    /// </summary>
    public void Dispose()
    {
        ApplyCancelCommand.Execute(null);

        NavManager.Dispose();
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

    private Repo? FindRepo(Guid repoId) => _repoRepository.Repos.FirstOrDefault(x => x.Id == repoId);


    public class Factory(IServiceProvider serviceProvider)
    {
        public InstancePageViewModel Create(Repo repo, LocalInstance instance)
            => ActivatorUtilities.CreateInstance<InstancePageViewModel>(serviceProvider, repo, instance);
    }
}
