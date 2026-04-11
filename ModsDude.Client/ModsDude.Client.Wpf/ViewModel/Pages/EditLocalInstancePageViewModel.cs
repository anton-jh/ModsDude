using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class EditLocalInstancePageViewModel : PageViewModel
{
    private readonly Repo _repo;
    private readonly NavigationLockService _navigationLockService;
    private readonly HashSet<string> _takenNames;
    private readonly LocalInstance _subject;
    private readonly IModalService _modalService;


    public EditLocalInstancePageViewModel(
        Repo repo,
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
        RepoName = repo.Name;

        InstanceSettingsEditor = new DynamicFormViewModel(false, subject.InstanceSettings, dialogService);

        InstanceSettingsEditor.Modified += OnInstanceSettingsModified;
        InstanceSettingsEditor.IsValidChanged += OnInstanceSettingsIsValidChanged;

        var _ = IsValid;
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name;

    public string RepoName { get; }

    public string OriginalName { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !_takenNames.Contains(Name) && InstanceSettingsEditor.IsValid;

    public DynamicFormViewModel InstanceSettingsEditor { get; }


    [RelayCommand]
    public async Task SaveChanges()
    {
        if (!IsValid)
        {
            var modal = ConfirmationDialogViewModel.ValidationErrors(GetValidationErrors());
            await _modalService.Show(modal);

            return;
        }

        var instanceSettings = InstanceSettingsEditor.ExtractResults();

        _navigationLockService.ReleaseLock(this);

        _subject.Update(Name, instanceSettings);
    }

    [RelayCommand]
    public async Task Delete()
    {
        var modal = ConfirmationDialogViewModel.ConfirmDelete(_subject.Name);

        await _modalService.Show(modal);

        if (modal.Result == true)
        {
            _navigationLockService.ReleaseLock(this);
            _repo.DeleteLocalInstance(_subject);
        }
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

    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Name is required.");
        }
        if (_takenNames.Contains(Name))
        {
            errors.Add("Name is taken.");
        }

        errors.AddRange(InstanceSettingsEditor.GetValidationErrors());

        return errors;
    }

    partial void OnNameChanged(string value)
    {
        _navigationLockService.AcquireLock(this);
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public EditLocalInstancePageViewModel Create(Repo repo, LocalInstance subject)
            => ActivatorUtilities.CreateInstance<EditLocalInstancePageViewModel>(serviceProvider, repo, subject);
    }
}
