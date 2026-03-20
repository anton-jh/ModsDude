using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class ProfilePageViewModelFactory(
    IServiceProvider services)
{
    public ProfilePageViewModel Create(ProfileDto profile)
    {
        return new(
            profile,
            services.GetRequiredService<ProfileService>(),
            services.GetRequiredService<NavigationLockService>());
    }
}
