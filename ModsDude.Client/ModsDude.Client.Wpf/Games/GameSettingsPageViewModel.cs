using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.ComponentModel;

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
    private readonly IToastService _toasts;
    private readonly ILogger<GameSettingsPageViewModel> _logger;


    public GameSettingsPageViewModel(
        Repo repo,
        Game subject,
        IAsyncRelayCommand back,
        IGameRepository gameRepository,
        IFilePickerService filePickerService,
        IModalService modalService,
        IToastService toasts,
        INavigationLockService navigationLockService,
        ILogger<GameSettingsPageViewModel> logger)
    {
        _logger = logger;
        _repo = repo;
        _subject = subject;
        BackCommand = back;
        _gameRepository = gameRepository;
        _modalService = modalService;
        _toasts = toasts;
        _navigationLockService = navigationLockService;
        GameName = subject.Name;
        RepoName = repo.Name;

        LocalSettingsEditor = new DynamicFormViewModel(true, subject.GetLocalSettings(repo.Adapter), filePickerService);

        LocalSettingsEditor.PropertyChanged += OnLocalSettingsChanged;
    }


    public string RepoName { get; }

    public string GameName { get; }

    /// <summary>
    /// Said rather than left to be inferred: these are the only settings in the app nobody else in the
    /// repo sees.
    /// </summary>
    public string Summary =>
        $"Where '{GameName}' is installed on this machine. Not shared with '{RepoName}'.";

    public bool IsValid => LocalSettingsEditor.IsValid && FindFolderConflict() is null;

    public DynamicFormViewModel LocalSettingsEditor { get; }

    public IAsyncRelayCommand BackCommand { get; }


    public bool HasChanges => LocalSettingsEditor.HasChanges;


    [RelayCommand(CanExecute = nameof(HasChanges))]
    public async Task SaveChanges()
    {
        if (!IsValid)
        {
            var modal = ConfirmationModalViewModel.ValidationErrors(GetValidationErrors());
            await _modalService.Show(modal);

            return;
        }

        try
        {
            _gameRepository.Update(_subject, _repo.Adapter, LocalSettingsEditor.ExtractResults());
        }
        catch (UserFriendlyException exception)
        {
            _logger.LogInformation("Settings of {Game} were not saved: {Reason}", _subject.Identity, exception.DeveloperMessage);

            await _modalService.Show(ConfirmationModalViewModel.Refusal("Settings not saved", exception.Describe()));

            return;
        }

        LocalSettingsEditor.MarkSaved();

        _toasts.Show($"Saved the settings for {GameName}");

        await BackCommand.ExecuteAsync(null);
    }

    public void Dispose()
    {
        _navigationLockService.ReleaseLock(this);
        LocalSettingsEditor.PropertyChanged -= OnLocalSettingsChanged;
        LocalSettingsEditor.Dispose();
    }


    private void OnLocalSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DynamicFormViewModel.IsValid):
                OnPropertyChanged(nameof(IsValid));
                break;

            case nameof(DynamicFormViewModel.HasChanges):
                OnPropertyChanged(nameof(HasChanges));
                SaveChangesCommand.NotifyCanExecuteChanged();
                _navigationLockService.SetLock(this, HasChanges);
                break;
        }
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
