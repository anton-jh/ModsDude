using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class ProfilePageViewModel(
    RepoModel repo,
    ProfileDto profile,
    ProfileService profileService,
    NavigationLockService navigationLockService,
    IModalService modalService)
    : PageViewModel, IDisposable
{
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
    }
    
    [RelayCommand]
    public async Task DeleteRepo(CancellationToken cancellationToken)
    {
        if (await ConfirmDelete())
        {
            navigationLockService.ReleaseLock(this);
            await profileService.DeleteProfile(profile.RepoId, profile.Id, cancellationToken);
        }
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }


    private async Task<bool> ConfirmDelete()
    {
        var modal = new ConfirmationDialogViewModel(
            "Really?",
            $"Are you sure you want to delete '{OriginalName}'.\nThis action cannot be undone!",
            IconKind.Warning,
            "Delete",
            "Keep");

        await modalService.Show(modal);

        return modal.Result;
    }
}
