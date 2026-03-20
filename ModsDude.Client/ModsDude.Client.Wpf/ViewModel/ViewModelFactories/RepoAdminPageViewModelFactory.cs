using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
public class RepoAdminPageViewModelFactory(
    IServiceProvider services)
{
    public RepoAdminPageViewModel Create(RepoModel repo)
    {
        return new RepoAdminPageViewModel(
            repo,
            services.GetRequiredService<RepoService>(),
            services.GetRequiredService<NavigationLockService>());
    }
}
