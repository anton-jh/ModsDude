using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;

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


    [RelayCommand]
    public async Task DeleteRepo(CancellationToken cancellationToken)
    {
        navigationLockService.ReleaseLock(this);
        await profileService.DeleteProfile(profile.RepoId, profile.Id, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        navigationLockService.ReleaseLock(this);
        await profileService.UpdateProfile(profile.RepoId, profile.Id, Name, cancellationToken);
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }
}
