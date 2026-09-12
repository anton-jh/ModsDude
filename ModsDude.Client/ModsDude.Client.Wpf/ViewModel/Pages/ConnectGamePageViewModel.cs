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
    private readonly HashSet<string> _takenNames;


    public ConnectGamePageViewModel(
        Repo repo,
        GameRepository gameRepository,
        IDialogService dialogService,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        // Names are unique within the scope, not within the repo: the same games are offered
        // under every repo targeting this game.
        var gamesInScope = gameRepository.GetByScope(repo.Scope).ToList();

        _name = gamesInScope.Count == 0 ? "Game" : "";
        _repo = repo;
        _gameRepository = gameRepository;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        _takenNames = gamesInScope.Select(x => x.Name).Distinct().ToHashSet();
        RepoName = _repo.Name;

        LocalSettingsEditor = new DynamicFormViewModel(false, repo.Adapter.GetLocalSettingsTemplate(), dialogService);
        LocalSettingsEditor.Modified += OnLocalSettingsModified;
    }


    public string RepoName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !_takenNames.Contains(Name) && LocalSettingsEditor.IsValid && FindFolderConflict() is null;

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
    /// Checked across every scope, since two games' games can name the same folder and only one
    /// of them can own it. Only asked of settings that are valid in their own right - the adapter
    /// refuses to hydrate anything else.
    /// </summary>
    private Game? FindFolderConflict()
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
        if (_takenNames.Contains(Name))
        {
            errors.Add("Name is taken.");
        }

        errors.AddRange(LocalSettingsEditor.GetValidationErrors());

        if (FindFolderConflict() is Game owner)
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
        public ConnectGamePageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<ConnectGamePageViewModel>(serviceProvider, repo);
    }
}
