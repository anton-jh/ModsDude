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
    : PageViewModel
{
    [ObservableProperty]
    private string _name = profile.Name;

    public string OriginalName => profile.Name;

    [RelayCommand]
    public async Task DeleteRepo(CancellationToken cancellationToken)
    {
        await profileService.DeleteProfile(profile.RepoId, profile.Id, cancellationToken);
    }

    [RelayCommand]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await profileService.UpdateProfile(profile.RepoId, profile.Id, Name, cancellationToken);
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }
}
