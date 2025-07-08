using Microsoft.Extensions.DependencyInjection;
using ModsDude.WindowsClient.Model.Models;
using ModsDude.WindowsClient.Model.Services;
using ModsDude.WindowsClient.ViewModel.Pages;

namespace ModsDude.WindowsClient.ViewModel.ViewModelFactories;
public class RepoAdminPageViewModelFactory(
    IServiceProvider services)
{
    public RepoAdminPageViewModel Create(RepoModel repo)
    {
        return new RepoAdminPageViewModel(
            services.GetRequiredService<RepoService>(),
            repo);
    }
}
