using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class ConnectGamePageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly GameRepository _gameRepository;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly bool _alreadyConnected;


    public ConnectGamePageViewModel(
        Repo repo,
        GameRepository gameRepository,
        IDialogService dialogService,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        // One game per identity, so connecting a second one is refused rather than offered - see
        // GameRepository.Create. The name stays a free-text label until slice 5 takes the field away
        // and the adapter's own display name stands in for it.
        _alreadyConnected = gameRepository.Find(repo.Scope) is not null;

        _name = "Game";
        _repo = repo;
        _gameRepository = gameRepository;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        RepoName = _repo.Name;

        LocalSettingsEditor = new DynamicFormViewModel(false, repo.Adapter.GetLocalSettingsTemplate(), dialogService);
        LocalSettingsEditor.Modified += OnLocalSettingsModified;
    }


    public string RepoName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

    public bool IsValid => _alreadyConnected is false && string.IsNullOrWhiteSpace(Name) is false && LocalSettingsEditor.IsValid && FindFolderConflict() is null;

    public DynamicFormViewModel LocalSettingsEditor { get; }

    [RelayCommand]
    public async Task SaveChanges()
    {
        if (!IsValid)
        {
            var modal = ConfirmationDialogViewModel.ValidationErrors(GetValidationErrors());
            await _modalService.Show(modal);

            return;
        }

        _gameRepository.Create(_repo.Adapter, Name, LocalSettingsEditor.ExtractResults());

        _navigationLockService.ReleaseLock(this);
    }

    public void Dispose()
    {
        _navigationLockService.Dispose();
        LocalSettingsEditor.Modified -= OnLocalSettingsModified;
        LocalSettingsEditor.Dispose();
    }


    private void OnLocalSettingsModified(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsValid));
        _navigationLockService.AcquireLock(this);
    }

    /// <summary>
    /// Checked across every game, since two of them can name the same folder and only one
    /// can own it - and across this one's own targets. Only asked of settings that are valid in their
    /// own right - the adapter refuses to hydrate anything else.
    /// </summary>
    private FolderClaim? FindFolderConflict()
    {
        return LocalSettingsEditor.IsValid
            ? _gameRepository.FindFolderConflict(_repo.Adapter, LocalSettingsEditor.ExtractResults())
            : null;
    }

    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Name is required.");
        }

        errors.AddRange(LocalSettingsEditor.GetValidationErrors());

        if (_alreadyConnected)
        {
            errors.Add("This game is already connected on this machine.");
        }

        if (FindFolderConflict() is FolderClaim claim)
        {
            errors.Add(claim.Describe());
        }

        return errors;
    }

    partial void OnNameChanged(string value)
    {
        _navigationLockService.AcquireLock(this);
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ConnectGamePageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<ConnectGamePageViewModel>(serviceProvider, repo);
    }
}
