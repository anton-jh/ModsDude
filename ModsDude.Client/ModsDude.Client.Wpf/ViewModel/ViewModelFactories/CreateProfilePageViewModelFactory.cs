using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class CreateProfilePageViewModelFactory(
    ProfileService profileService)
{
    public CreateProfilePageViewModel Create(RepoModel repo)
    {
        return new(repo, profileService);
    }
}
