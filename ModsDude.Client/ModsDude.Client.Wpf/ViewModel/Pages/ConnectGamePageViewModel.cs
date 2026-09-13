using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// Connecting this machine's installation of the game a repo is about.
/// </summary>
/// <remarks>
/// <b>It is the settings form and nothing else.</b> There used to be a name box above it, defaulting
/// to "Game" and checked for uniqueness within the scope; both went in slice 5. A game is called what
/// its adapter calls it - Farming Simulator 25 - and one is configured once per identity, so there
/// was nothing left for a typed name to distinguish and nothing it could say that
/// <c>Adapter.GameDisplayName</c> did not already.
/// </remarks>
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
        // GameRepository.Create.
        _alreadyConnected = gameRepository.Find(repo.Scope) is not null;

        _repo = repo;
        _gameRepository = gameRepository;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        RepoName = _repo.Name;
        GameName = _repo.Adapter.GameDisplayName;

        LocalSettingsEditor = new DynamicFormViewModel(false, repo.Adapter.GetLocalSettingsTemplate(), dialogService);
        LocalSettingsEditor.Modified += OnLocalSettingsModified;
    }


    public string RepoName { get; }

    /// <summary>
    /// What is being connected, said rather than asked. It is the adapter's own name for the game
    /// these base settings configure it for, which is the same name the sidebar groups this repo
    /// under.
    /// </summary>
    public string GameName { get; }

    public bool IsValid => _alreadyConnected is false && LocalSettingsEditor.IsValid && FindFolderConflict() is null;

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

        _gameRepository.Create(_repo.Adapter, LocalSettingsEditor.ExtractResults());

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


    public class Factory(IServiceProvider serviceProvider)
    {
        public ConnectGamePageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<ConnectGamePageViewModel>(serviceProvider, repo);
    }
}
