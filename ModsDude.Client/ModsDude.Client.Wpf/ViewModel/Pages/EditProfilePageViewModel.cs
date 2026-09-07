using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class EditProfilePageViewModel(
    Repo repo,
    ProfileDto profile,
    ProfileService profileService,
    NavigationLockService navigationLockService,
    IModalService modalService)
    : PageViewModel, IDisposable
{
    private readonly NavigationManager _navigationManager = new(navigationLockService, modalService);
    private readonly MenuItemViewModel _modListEditorMenuItem = new(
        "", () => new ExamplePageViewModel(repo.Name, "Mods"));


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name = profile.Name;

    public string RepoName => repo.Name;
    public string OriginalName => profile.Name;
    public bool IsValid => !string.IsNullOrWhiteSpace(Name);


    [RelayCommand(CanExecute = nameof(IsValid))]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        navigationLockService.ReleaseLock(this);
        await profileService.UpdateProfile(profile.RepoId, profile.Id, Name, cancellationToken);

        // The DTO is updated in place rather than replaced, so nothing else announces the new name.
        OnPropertyChanged(nameof(OriginalName));
    }
    
    /// <summary>
    /// Puts the profile in the repo's Archive rather than deleting it. A profile carries a history
    /// that makes every one of its revisions reproducible, and that is not something to lose from a
    /// page somebody opened to rename it.
    /// </summary>
    [RelayCommand]
    public async Task ArchiveProfile(CancellationToken cancellationToken)
    {
        if (await ConfirmArchive())
        {
            navigationLockService.ReleaseLock(this);
            await profileService.ArchiveProfile(profile.RepoId, profile.Id, cancellationToken);
        }
    }

    [RelayCommand]
    public void OpenModListEditor()
    {
        _navigationManager.Selected = _modListEditorMenuItem;
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }


    private async Task<bool> ConfirmArchive()
    {
        var modal = ConfirmationDialogViewModel.ConfirmArchive(OriginalName, "profile");

        await modalService.Show(modal);

        return modal.Result;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public EditProfilePageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<EditProfilePageViewModel>(serviceProvider, repo, profile);
    }
}
