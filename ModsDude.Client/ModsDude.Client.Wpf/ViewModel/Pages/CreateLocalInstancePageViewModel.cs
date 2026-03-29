using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;


public partial class CreateLocalInstancePageViewModel
    : PageViewModel, IDisposable
{
    private readonly RepoModel _repo;
    private readonly NavigationLockService _navigationLockService;
    private readonly LocalInstanceService _localInstanceService;
    private readonly HashSet<string> _takenNames;


    public CreateLocalInstancePageViewModel(
        RepoModel repo,
        IGameAdapterIndex gameAdapterIndex,
        IDialogService dialogService,
        NavigationLockService navigationLockService,
        LocalInstanceService localInstanceService)
    {
        var existingInstances = localInstanceService.GetByRepoId(repo.Id);
        _name = existingInstances.Count == 0 ? "Game" : "";
        _repo = repo;
        _navigationLockService = navigationLockService;
        _localInstanceService = localInstanceService;
        _takenNames = existingInstances.Select(x => x.Name).Distinct().ToHashSet();
        RepoName = _repo.Name;

        InstanceSettingsEditor = new DynamicFormViewModel(false, gameAdapterIndex.GetById(repo.AdapterId).InstanceSettingsTemplate, dialogService);
        InstanceSettingsEditor.Modified += OnInstanceSettingsModified;
        InstanceSettingsEditor.IsValidChanged += OnInstanceSettingsIsValidChanged;
    }


    public string RepoName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !_takenNames.Contains(Name) && InstanceSettingsEditor.IsValid;

    public DynamicFormViewModel InstanceSettingsEditor { get; }


    [RelayCommand(CanExecute = nameof(IsValid))]
    public void SaveChanges()
    {
        if (!IsValid)
        {
            return;
        }

        _localInstanceService.Create(_repo, Name, InstanceSettingsEditor.ExtractResults());

        _navigationLockService.ReleaseLock(this);
    }

    public void Dispose()
    {
        _navigationLockService.Dispose();
        InstanceSettingsEditor.Modified -= OnInstanceSettingsModified;
        InstanceSettingsEditor.IsValidChanged -= OnInstanceSettingsIsValidChanged;
        InstanceSettingsEditor.Dispose();
    }


    private void OnInstanceSettingsModified(object? sender, EventArgs e)
    {
        _navigationLockService.AcquireLock(this);
    }

    private void OnInstanceSettingsIsValidChanged(object? sender, EventArgs e)
    {
        SaveChangesCommand.NotifyCanExecuteChanged();
    }

    partial void OnNameChanged(string value)
    {
        _navigationLockService.AcquireLock(this);
    }
}
