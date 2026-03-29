using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
public class RepoPageViewModelFactory(
    IServiceProvider serviceProvider)
{
    public RepoPageViewModel Create(RepoModel repo)
    {
        return new RepoPageViewModel(
            repo,
            serviceProvider.GetRequiredService<RepoAdminPageViewModelFactory>(),
            serviceProvider.GetRequiredService<CreateProfilePageViewModelFactory>(),
            serviceProvider.GetRequiredService<ProfilePageViewModelFactory>(),
            serviceProvider.GetRequiredService<ProfileService>(),
            serviceProvider.GetRequiredService<NavigationLockService>(),
            serviceProvider.GetRequiredService<IModalService>(),
            serviceProvider.GetRequiredService<CreateLocalInstancePageViewModelFactory>(),
            serviceProvider.GetRequiredService<EditLocalInstancePageViewModelFactory>(),
            serviceProvider.GetRequiredService<LocalInstanceService>());
    }
}
