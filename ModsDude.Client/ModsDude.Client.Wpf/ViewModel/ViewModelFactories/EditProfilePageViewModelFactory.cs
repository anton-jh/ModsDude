using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class EditProfilePageViewModelFactory(
    IServiceProvider serviceProvider)
{
    public EditProfilePageViewModel Create(RepoModel repo, ProfileDto profile)
    {
        return new(
            repo, profile,
            serviceProvider.GetRequiredService<ProfileService>(),
            serviceProvider.GetRequiredService<NavigationLockService>(),
            serviceProvider.GetRequiredService<IModalService>());
    }
}
