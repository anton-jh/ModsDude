using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class ProfilePageViewModelFactory(
    IServiceProvider services)
{
    public ProfilePageViewModel Create(ProfileDto profile)
    {
        return new(profile);
    }
}
