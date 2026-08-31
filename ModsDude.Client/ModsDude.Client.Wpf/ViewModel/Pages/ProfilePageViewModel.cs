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
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The profile's shell, and the profile-side half of activation.
/// </summary>
/// <remarks>
/// <para>
/// The activation control sits here rather than on Overview so that it is present on every sub-page.
/// From this end the profile is fixed and the instance is chosen, which is why there is a dropdown at
/// all - and none when the repo offers a single instance, which is the common case for most games.
/// </para>
/// <para>
/// It is <b>labelled for what it will do</b>: an instance already on this profile is being re-applied,
/// one on another profile or none is being moved, and moving it uninstalls whatever the previous
/// profile put in the folder. See docs/07-mod-sync-design.md#activating-a-profile-on-an-instance.
/// </para>
/// </remarks>
public partial class ProfilePageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly LocalInstanceRepository _localInstanceRepository;
    private readonly ProfileApplyService _applyService;
    private readonly InstanceDriftMonitor _driftMonitor;
    private readonly MenuItemViewModel _modsMenuItem;

    private ProfileModsEditorPageViewModel? _openModsEditor;


    public ProfilePageViewModel(
        Repo repo,
        ProfileDto profile,
        NavigationManager navigationManager,
        LocalInstanceRepository localInstanceRepository,
        ProfileApplyService applyService,
        InstanceDriftMonitor driftMonitor,
        ProfileOverviewPageViewModel.Factory profileOverviewPageViewModelFactory,
        EditProfilePageViewModel.Factory editProfilePageViewModelFactory,
        ProfileModsEditorPageViewModel.Factory profileModsEditorPageViewModelFactory)
    {
        _repo = repo;
        _profile = profile;
        _localInstanceRepository = localInstanceRepository;
        _applyService = applyService;
        _driftMonitor = driftMonitor;

        _modsMenuItem = new MenuItemViewModel("Mods", () => profileModsEditorPageViewModelFactory.Create(repo, profile));

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Overview", () => profileOverviewPageViewModelFactory.Create(repo, profile)),
            _modsMenuItem,
            new MenuItemViewModel("Manage", () => editProfilePageViewModelFactory.Create(repo, profile))
        ];

        Instances = [];

        NavManager.Selected = MenuItems.First();
        NavManager.PropertyChanged += OnNavigationChanged;

        _repo.LocalInstances.CollectionChanged += OnInstancesChanged;

        RefreshInstances();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }

    /// <summary>The instances this repo offers, which are compatible with it by construction.</summary>
    public ObservableCollection<LocalInstance> Instances { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    [NotifyPropertyChangedFor(nameof(ActivationDescription))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private LocalInstance? _selectedInstance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceChoice))]
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

    /// <summary>With one instance there is nothing to pick, so the dropdown does not appear at all.</summary>
    public bool HasInstanceChoice => InstanceCount > 1;

    public bool HasActivationStatus => ActivationStatus is not null;

    public InstanceActivationKind ActivationKind => InstanceActivation.Describe(
        SelectedInstance?.ActiveProfile,
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

            if (SelectedInstance is not LocalInstance instance)
            {
                return "";
            }

            return ActivationKind is InstanceActivationKind.Reapply
                ? $"'{instance.Name}' already follows this profile. Applying it again makes the mod folder match."
                : $"'{instance.Name}' will start following this profile. Whatever its current profile put in the mod folder is taken back out.";
        }
    }


    [RelayCommand(CanExecute = nameof(CanActivate), IncludeCancelCommand = true)]
    private async Task Activate(CancellationToken cancellationToken)
    {
        if (SelectedInstance is not LocalInstance instance)
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
                instance,
                _profile.Id,
                _profile.Name,
                // Moving an instance onto a different profile takes the previous one's mods back out,
                // so the plan is shown first. A re-apply has nothing extra to disclose.
                confirmPlan: kind is InstanceActivationKind.Activate,
                progress: null,
                cancellationToken);

            // The intent is recorded whatever the folder ended up doing: an instance that could not be
            // reached is still meant to follow this profile, and the drift notice covers the rest.
            if (outcome.Status is not ProfileApplyStatus.Declined)
            {
                _localInstanceRepository.SetActiveProfile(instance, target);
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

    private bool CanActivate() => SelectedInstance is not null && IsApplying is false && BlockedByUnsavedChanges is false;


    /// <summary>Selects the Mods sub-page, for a deep link from the drift notice.</summary>
    public bool TrySelectMods()
    {
        if (ReferenceEquals(NavManager.Selected, _modsMenuItem) is false)
        {
            NavManager.Selected = _modsMenuItem;
        }

        return ReferenceEquals(NavManager.Selected, _modsMenuItem);
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
        _repo.LocalInstances.CollectionChanged -= OnInstancesChanged;

        NavManager.Dispose();
    }


    private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshInstances();
    }

    private void RefreshInstances()
    {
        var previous = SelectedInstance;

        Instances.Clear();

        foreach (var instance in _repo.LocalInstances)
        {
            Instances.Add(instance);
        }

        InstanceCount = Instances.Count;

        // Prefer one already on this profile: with several instances the likeliest intent is
        // re-applying, and that is also the one the label has to get right on first sight.
        SelectedInstance = previous is not null && Instances.Contains(previous) ? previous : Instances
            .FirstOrDefault(x => x.ActiveProfile == new ActiveProfile(_repo.Id, _profile.Id))
            ?? Instances.FirstOrDefault();

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
