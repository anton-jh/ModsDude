using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The game's adapter settings - which folders it reaches - and disconnecting it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reached straight from the repo's menu, as <em>Configure game</em>.</b> It had a shell of
/// its own over Sync, Saves and Manage, under an entry titled with the game's own name; all three
/// sub-pages are gone and so is the shell, and what is left is this form. That is the whole of the
/// point: a local installation is not a fourth kind of entity to navigate into beside repos and
/// profiles, it is a handful of folder paths this machine remembers. Where the game <em>stands</em>
/// is on the repo's Overview, and what to do about it is on a profile's page or the app-level notice.
/// </para>
/// <para>
/// The active profile used to be here too. It is on the profile's own page now, which is the end of
/// activation where the target is fixed and the profile is chosen - and only there, because two
/// places to set one thing is how they come to disagree.
/// </para>
/// <para>
/// So did a name box. A game is called what its adapter calls it, so there is nothing here to type:
/// the settings are the whole of what a machine decides about a game, and every folder it reaches
/// falls out of them.
/// </para>
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
        _repo = repo;
        _subject = subject;
        _gameRepository = gameRepository;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        GameName = subject.Name;
        RepoName = repo.Name;

        LocalSettingsEditor = new DynamicFormViewModel(true, subject.GetLocalSettings(repo.Adapter), dialogService);

        LocalSettingsEditor.Modified += OnLocalSettingsModified;
        LocalSettingsEditor.IsValidChanged += OnLocalSettingsIsValidChanged;

        var _ = IsValid;
    }


    public string RepoName { get; }

    public string GameName { get; }

    /// <summary>
    /// What this page is about, in one sentence: <em>your</em> copy of the game, not anything the
    /// repo holds.
    /// </summary>
    /// <remarks>
    /// Worth a line rather than left to be inferred from the folder pickers. Every other entry in
    /// this menu is about the repo and is the same for everybody in it; this one is the only local
    /// thing there, and the settings on it are the only settings in the app nobody else ever sees.
    /// </remarks>
    public string Summary =>
        $"Where '{GameName}' is installed on this machine. These settings belong to this machine alone - " +
        $"nobody else in '{RepoName}' sees them, and changing them changes nothing in the repo.";

    public bool IsValid => LocalSettingsEditor.IsValid && FindFolderConflict() is null;

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

        _gameRepository.Update(_subject, _repo.Adapter, localSettings);
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

        errors.AddRange(LocalSettingsEditor.GetValidationErrors());

        if (FindFolderConflict() is FolderClaim claim)
        {
            errors.Add(claim.Describe());
        }

        return errors;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public GameSettingsPageViewModel Create(Repo repo, Game subject)
            => ActivatorUtilities.CreateInstance<GameSettingsPageViewModel>(serviceProvider, repo, subject);
    }
}
