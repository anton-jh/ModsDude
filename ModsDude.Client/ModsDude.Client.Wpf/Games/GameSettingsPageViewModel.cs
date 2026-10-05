using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;

namespace ModsDude.Client.Wpf.Games;

/// <summary>
/// The local settings of a connected game: where it is installed on this machine. Reached from the
/// repo Overview's "This machine" card, which is also where it is disconnected.
/// </summary>
public partial class GameSettingsPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly IGameRepository _gameRepository;
    private readonly INavigationLockService _navigationLockService;
    private readonly Game _subject;
    private readonly IModalService _modalService;


    public GameSettingsPageViewModel(
        Repo repo,
        Game subject,
        IAsyncRelayCommand back,
        IGameRepository gameRepository,
        IFilePickerService filePickerService,
        IModalService modalService,
        INavigationLockService navigationLockService)
    {
        _repo = repo;
        _subject = subject;
        BackCommand = back;
        _gameRepository = gameRepository;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        GameName = subject.Name;
        RepoName = repo.Name;

        LocalSettingsEditor = new DynamicFormViewModel(true, subject.GetLocalSettings(repo.Adapter), filePickerService);

        LocalSettingsEditor.Modified += OnLocalSettingsModified;
        LocalSettingsEditor.IsValidChanged += OnLocalSettingsIsValidChanged;
    }


    public string RepoName { get; }

    public string GameName { get; }

    /// <summary>
    /// Said rather than left to be inferred: these are the only settings in the app nobody else in the
    /// repo sees.
    /// </summary>
    public string Summary =>
        $"Where '{GameName}' is installed on this machine. These settings belong to this machine alone - " +
        $"nobody else in '{RepoName}' sees them, and changing them changes nothing in the repo.";

    public bool IsValid => LocalSettingsEditor.IsValid && FindFolderConflict() is null;

    public DynamicFormViewModel LocalSettingsEditor { get; }

    public IAsyncRelayCommand BackCommand { get; }


    [RelayCommand]
    public async Task SaveChanges()
    {
        if (!IsValid)
        {
            var modal = ConfirmationModalViewModel.ValidationErrors(GetValidationErrors());
            await _modalService.Show(modal);

            return;
        }

        _gameRepository.Update(_subject, _repo.Adapter, LocalSettingsEditor.ExtractResults());

        _navigationLockService.ReleaseLock(this);

        await BackCommand.ExecuteAsync(null);
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
    /// Checked across every game, since two of them can name the same folder and only one can own
    /// it, and across this one's own targets. Only asked of settings that are valid in their own
    /// right - the adapter refuses to hydrate anything else.
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

        errors.AddRange(LocalSettingsEditor.GetValidationErrors());

        if (FindFolderConflict() is FolderClaim claim)
        {
            errors.Add(claim.Describe());
        }

        return errors;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public GameSettingsPageViewModel Create(Repo repo, Game subject, IAsyncRelayCommand back)
            => ActivatorUtilities.CreateInstance<GameSettingsPageViewModel>(serviceProvider, repo, subject, back);
    }
}
