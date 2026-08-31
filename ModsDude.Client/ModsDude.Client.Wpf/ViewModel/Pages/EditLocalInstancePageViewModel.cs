using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The instance's name and adapter settings, and disconnecting it.
/// </summary>
/// <remarks>
/// The active profile used to be here too. It is on <see cref="InstancePageViewModel"/> now, beside
/// the drift status and the Re-apply it belongs with - and only there, because two places to set one
/// thing is how they come to disagree.
/// </remarks>
public partial class EditLocalInstancePageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly LocalInstanceRepository _localInstanceRepository;
    private readonly NavigationLockService _navigationLockService;
    private readonly HashSet<string> _takenNames;
    private readonly LocalInstance _subject;
    private readonly IModalService _modalService;


    public EditLocalInstancePageViewModel(
        Repo repo,
        LocalInstance subject,
        LocalInstanceRepository localInstanceRepository,
        IDialogService dialogService,
        IModalService modalService,
        NavigationLockService navigationLockService)
    {
        _name = subject.Name;
        _repo = repo;
        _subject = subject;
        _localInstanceRepository = localInstanceRepository;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        _takenNames = localInstanceRepository.GetByScope(repo.Scope)
            .Where(x => x.Id != subject.Id)
            .Select(x => x.Name)
            .Distinct()
            .ToHashSet();
        OriginalName = subject.Name;
        RepoName = repo.Name;

        InstanceSettingsEditor = new DynamicFormViewModel(true, subject.GetInstanceSettings(repo.Adapter), dialogService);

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

        var instanceSettings = InstanceSettingsEditor.ExtractResults();

        _navigationLockService.ReleaseLock(this);

        _localInstanceRepository.Update(_subject, _repo.Adapter, Name, instanceSettings);
    }

    [RelayCommand]
    public async Task Delete()
    {
        var modal = ConfirmationDialogViewModel.ConfirmDelete(_subject.Name);

        await _modalService.Show(modal);

        if (modal.Result == true)
        {
            _navigationLockService.ReleaseLock(this);
            _localInstanceRepository.Delete(_subject);
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

    /// <summary>
    /// Checked across every scope, since two games' instances can name the same folder and only one
    /// of them can own it. Only asked of settings that are valid in their own right - the adapter
    /// refuses to hydrate anything else.
    /// </summary>
    private LocalInstance? FindFolderConflict()
    {
        return InstanceSettingsEditor.IsValid
            ? _localInstanceRepository.FindFolderConflict(_repo.Adapter, InstanceSettingsEditor.ExtractResults(), _subject.Id)
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
        public EditLocalInstancePageViewModel Create(Repo repo, LocalInstance subject)
            => ActivatorUtilities.CreateInstance<EditLocalInstancePageViewModel>(serviceProvider, repo, subject);
    }
}
