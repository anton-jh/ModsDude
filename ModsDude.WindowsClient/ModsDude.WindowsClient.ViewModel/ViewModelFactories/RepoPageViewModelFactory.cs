using Microsoft.Extensions.DependencyInjection;
using ModsDude.WindowsClient.Model.Models;
using ModsDude.WindowsClient.Model.Services;
using ModsDude.WindowsClient.ViewModel.Pages;

namespace ModsDude.WindowsClient.ViewModel.ViewModelFactories;
public class RepoPageViewModelFactory(
    IServiceProvider services)
{
    public RepoPageViewModel Create(RepoModel repo)
    {
        return new RepoPageViewModel(
            repo,
            services.GetRequiredService<RepoAdminPageViewModelFactory>(),
            services.GetRequiredService<ProfileService>());
    }
}
