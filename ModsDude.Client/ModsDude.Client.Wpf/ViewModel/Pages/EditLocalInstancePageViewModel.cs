using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class EditLocalInstancePageViewModel : PageViewModel
{
    private readonly RepoModel _repo;
    private readonly NavigationLockService _navigationLockService;
    private readonly HashSet<string> _takenNames;
    private readonly LocalInstance _subject;
    private readonly IModalService _modalService;


    public EditLocalInstancePageViewModel(
        RepoModel repo,
        LocalInstance subject,
        IGameAdapterIndex gameAdapterIndex,
        IDialogService dialogService,
        IModalService modalService,
        NavigationLockService navigationLockService)
    {
        _name = subject.Name;
        _repo = repo;
        _subject = subject;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        _takenNames = repo.LocalInstances.Select(x => x.Name).Distinct().ToHashSet();
        OriginalName = subject.Name;

        InstanceSettingsEditor = new DynamicFormViewModel(false, gameAdapterIndex.GetById(repo.AdapterId).InstanceSettingsTemplate, dialogService);
        InstanceSettingsEditor.Modified += OnInstanceSettingsModified;
        InstanceSettingsEditor.IsValidChanged += OnInstanceSettingsIsValidChanged;
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

    public string OriginalName { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !_takenNames.Contains(Name) && InstanceSettingsEditor.IsValid;

    public DynamicFormViewModel InstanceSettingsEditor { get; }


    [RelayCommand(CanExecute = nameof(IsValid))]
    public void SaveChanges()
    {
        if (!IsValid)
        {
            return;
        }

        var instanceSettings = InstanceSettingsEditor.ExtractResults();

        _subject.Name = Name;
        _subject.AdapterInstanceSettings = instanceSettings;

        _navigationLockService.ReleaseLock(this);
    }

    [RelayCommand]
    public async Task Delete()
    {
        var modal = ConfirmationDialogViewModel.ConfirmDelete(_subject.Name);

        await _modalService.Show(modal);

        if (modal.Result == true)
        {
            _repo.LocalInstances.Remove(_subject);
        }

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
        OnPropertyChanged(nameof(IsValid));
    }

    partial void OnNameChanged(string value)
    {
        _navigationLockService.AcquireLock(this);
    }
}
