using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class CreateProfilePageViewModel(
    RepoModel repo,
    IProfilesClient profilesClient)
    : PageViewModel
{
    private readonly RepoModel _repo = repo;

    [ObservableProperty]
    private string _name = "";


    [RelayCommand]
    public async Task Submit(CancellationToken cancellationToken)
    {
        await profilesClient.CreateProfileV1Async(_repo.Id, new()
        {
            Name = Name,
        }, cancellationToken);
    }
}
