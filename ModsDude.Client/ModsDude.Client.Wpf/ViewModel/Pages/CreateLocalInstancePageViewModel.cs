using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class CreateLocalInstancePageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly HashSet<string> _takenNames;


    public CreateLocalInstancePageViewModel(
        Repo repo,
        IGameAdapterIndex gameAdapterIndex,
        IDialogService dialogService,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        var existingInstances = repo.LocalInstances;
        _name = existingInstances.Count == 0 ? "Game" : "";
        _repo = repo;
        _navigationLockService = navigationLockService;
        _modalService = modalService;   
        _takenNames = existingInstances.Select(x => x.Name).Distinct().ToHashSet();
        RepoName = _repo.Name;

        InstanceSettingsEditor = new DynamicFormViewModel(false, repo.Adapter.GetInstanceSettingsTemplate(), dialogService);
        InstanceSettingsEditor.Modified += OnInstanceSettingsModified;
    }


    public string RepoName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

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

        _repo.CreateLocalInstance(Name, InstanceSettingsEditor.ExtractResults());

        _navigationLockService.ReleaseLock(this);
    }

    public void Dispose()
    {
        _navigationLockService.Dispose();
        InstanceSettingsEditor.Modified -= OnInstanceSettingsModified;
        InstanceSettingsEditor.Dispose();
    }


    private void OnInstanceSettingsModified(object? sender, EventArgs e)
    {
        _navigationLockService.AcquireLock(this);
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
        public CreateLocalInstancePageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<CreateLocalInstancePageViewModel>(serviceProvider, repo);
    }
}
