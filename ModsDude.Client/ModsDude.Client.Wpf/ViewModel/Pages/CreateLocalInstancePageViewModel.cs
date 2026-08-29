using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class CreateLocalInstancePageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly LocalInstanceRepository _localInstanceRepository;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly HashSet<string> _takenNames;


    public CreateLocalInstancePageViewModel(
        Repo repo,
        LocalInstanceRepository localInstanceRepository,
        IDialogService dialogService,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        // Names are unique within the scope, not within the repo: the same instances are offered
        // under every repo targeting this game.
        var instancesInScope = localInstanceRepository.GetByScope(repo.Scope).ToList();

        _name = instancesInScope.Count == 0 ? "Game" : "";
        _repo = repo;
        _localInstanceRepository = localInstanceRepository;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        _takenNames = instancesInScope.Select(x => x.Name).Distinct().ToHashSet();
        RepoName = _repo.Name;

        InstanceSettingsEditor = new DynamicFormViewModel(false, repo.Adapter.GetInstanceSettingsTemplate(), dialogService);
        InstanceSettingsEditor.Modified += OnInstanceSettingsModified;
    }


    public string RepoName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !_takenNames.Contains(Name) && InstanceSettingsEditor.IsValid && FindFolderConflict() is null;

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

        _localInstanceRepository.Create(_repo.Adapter, Name, InstanceSettingsEditor.ExtractResults());

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
        OnPropertyChanged(nameof(IsValid));
        _navigationLockService.AcquireLock(this);
    }

    /// <summary>
    /// Checked across every scope, since two games' instances can name the same folder and only one
    /// of them can own it. Only asked of settings that are valid in their own right - the adapter
    /// refuses to hydrate anything else.
    /// </summary>
    private LocalInstance? FindFolderConflict()
    {
        return InstanceSettingsEditor.IsValid
            ? _localInstanceRepository.FindFolderConflict(_repo.Adapter, InstanceSettingsEditor.ExtractResults())
            : null;
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

        if (FindFolderConflict() is LocalInstance owner)
        {
            errors.Add($"That folder already belongs to '{owner.Name}'.");
        }

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
