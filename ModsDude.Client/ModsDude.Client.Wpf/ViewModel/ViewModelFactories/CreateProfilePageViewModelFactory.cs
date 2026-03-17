using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class CreateProfilePageViewModelFactory(
    IProfilesClient profilesClient)
{
    public CreateProfilePageViewModel Create(RepoModel repo)
    {
        return new(repo, profilesClient);
    }
}
