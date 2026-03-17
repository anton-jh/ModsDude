using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class CreateProfilePageViewModelFactory(
    IServiceProvider services)
{
    public CreateProfilePageViewModel Create(RepoModel repo)
    {
        return new(repo, services.GetRequiredService<ProfileService>());
    }
}
