using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class ProfilePageViewModelFactory(
    IServiceProvider services)
{
    public ProfilePageViewModel Create(RepoModel repo, ProfileDto profile)
    {
        return new(
            repo,
            profile,
            services.GetRequiredService<ProfileService>(),
            services.GetRequiredService<NavigationLockService>(),
            services.GetRequiredService<IModalService>());
    }
}
