using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class CreateProfilePageViewModel(
    RepoModel repo,
    ProfileService profileService)
    : PageViewModel
{
    private readonly RepoModel _repo = repo;

    [ObservableProperty]
    private string _name = "";


    [RelayCommand]
    public async Task Submit(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        await profileService.CreateProfile(_repo.Id, Name, cancellationToken);
    }
}
