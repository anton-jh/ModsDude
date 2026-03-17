using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;

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
            services.GetRequiredService<ProfileService>());
    }
}
