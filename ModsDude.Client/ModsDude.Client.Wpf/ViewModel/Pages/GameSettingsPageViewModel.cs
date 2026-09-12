using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The game's name and adapter settings, and disconnecting it.
/// </summary>
/// <remarks>
/// The active profile used to be here too. It is on <see cref="GamePageViewModel"/> now, beside
/// the drift status and the Re-apply it belongs with - and only there, because two places to set one
/// thing is how they come to disagree.
/// </remarks>
public partial class GameSettingsPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly GameRepository _gameRepository;
    private readonly NavigationLockService _navigationLockService;
    private readonly Game _subject;
    private readonly IModalService _modalService;


    public GameSettingsPageViewModel(
        Repo repo,
        Game subject,
        GameRepository gameRepository,
        IDialogService dialogService,
        IModalService modalService,
        NavigationLockService navigationLockService)
    {
        _name = subject.Name;
        _repo = repo;
        _subject = subject;
        _gameRepository = gameRepository;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        OriginalName = subject.Name;
        RepoName = repo.Name;

        LocalSettingsEditor = new DynamicFormViewModel(true, subject.GetLocalSettings(repo.Adapter), dialogService);

        LocalSettingsEditor.Modified += OnLocalSettingsModified;
        LocalSettingsEditor.IsValidChanged += OnLocalSettingsIsValidChanged;

        var _ = IsValid;
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name;

    public string RepoName { get; }

    public string OriginalName { get; }

    public bool IsValid => string.IsNullOrWhiteSpace(Name) is false && LocalSettingsEditor.IsValid && FindFolderConflict() is null;

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

        var localSettings = LocalSettingsEditor.ExtractResults();

        _navigationLockService.ReleaseLock(this);

        _gameRepository.Update(_subject, _repo.Adapter, Name, localSettings);
    }

    [RelayCommand]
    public async Task Delete()
    {
        var modal = ConfirmationDialogViewModel.ConfirmDelete(_subject.Name);

        await _modalService.Show(modal);

        if (modal.Result == true)
        {
            _navigationLockService.ReleaseLock(this);
            _gameRepository.Delete(_subject);
        }
    }

    public void Dispose()
    {
        _navigationLockService.Dispose();
        LocalSettingsEditor.Modified -= OnLocalSettingsModified;
        LocalSettingsEditor.IsValidChanged -= OnLocalSettingsIsValidChanged;
        LocalSettingsEditor.Dispose();
    }


    private void OnLocalSettingsModified(object? sender, EventArgs e)
    {
        _navigationLockService.AcquireLock(this);
    }

    private void OnLocalSettingsIsValidChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsValid));
    }

    /// <summary>
    /// Checked across every game, since two of them can name the same folder and only one
    /// can own it - and across this one's own targets. Only asked of settings that are valid in their
    /// own right - the adapter refuses to hydrate anything else.
    /// </summary>
    private FolderClaim? FindFolderConflict()
    {
        return LocalSettingsEditor.IsValid
            ? _gameRepository.FindFolderConflict(_repo.Adapter, LocalSettingsEditor.ExtractResults(), _subject.Identity)
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
        public GameSettingsPageViewModel Create(Repo repo, Game subject)
            => ActivatorUtilities.CreateInstance<GameSettingsPageViewModel>(serviceProvider, repo, subject);
    }
}
