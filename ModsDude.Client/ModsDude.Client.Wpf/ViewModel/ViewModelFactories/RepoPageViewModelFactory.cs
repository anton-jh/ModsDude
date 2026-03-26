using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
public class RepoPageViewModelFactory(
    IServiceProvider services)
{
    public RepoPageViewModel Create(RepoModel repo)
    {
        return new RepoPageViewModel(
            repo,
            services.GetRequiredService<RepoAdminPageViewModelFactory>(),
            services.GetRequiredService<CreateProfilePageViewModelFactory>(),
            services.GetRequiredService<ProfilePageViewModelFactory>(),
            services.GetRequiredService<ProfileService>(),
            services.GetRequiredService<NavigationLockService>(),
            services.GetRequiredService<IModalService>(),
            services.GetRequiredService<CreateLocalInstancePageViewModelFactory>(),
            services.GetRequiredService<EditLocalInstancePageViewModelFactory>());
    }
}
