using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Services;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class ProfilePageViewModel(
    ProfileDto profile,
    ProfileService profileService,
    NavigationLockService navigationLockService)
    : PageViewModel, IDisposable
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name = profile.Name;

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
        if (!ConfirmDelete())
        {
            return;
        }

        navigationLockService.ReleaseLock(this);
        await profileService.DeleteProfile(profile.RepoId, profile.Id, cancellationToken);
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }


    private bool ConfirmDelete()
    {
        var result = System.Windows.Forms.MessageBox.Show(
            $"Are you sure you want to delete '{OriginalName}'.\nThis action cannot be undone!",
            "Really?",
            System.Windows.Forms.MessageBoxButtons.OKCancel,
            System.Windows.Forms.MessageBoxIcon.Warning,
            System.Windows.Forms.MessageBoxDefaultButton.Button2);

        return result == System.Windows.Forms.DialogResult.OK;
    }
}
