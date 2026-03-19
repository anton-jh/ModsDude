using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class ProfilePageViewModel
    : PageViewModel
{
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;


    public ProfilePageViewModel(
        ProfileDto profile,
        ProfileService profileService)
    {
        Name = profile.Name;
        _profile = profile;
        _profileService = profileService;
    }


    [ObservableProperty]
    private string _name;

    public string OriginalName => _profile.Name;

    [RelayCommand]
    public async Task DeleteRepo(CancellationToken cancellationToken)
    {
        await _profileService.DeleteProfile(_profile.RepoId, _profile.Id, cancellationToken);
    }

    [RelayCommand]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await _profileService.UpdateProfile(_profile.RepoId, _profile.Id, Name, cancellationToken);
    }
}
