using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class CreateLocalInstancePageViewModelFactory(
    IServiceProvider serviceProvider)
{
    public CreateLocalInstancePageViewModel Create(RepoModel repo)
    {
        return new(
            repo,
            serviceProvider.GetRequiredService<IGameAdapterIndex>(),
            serviceProvider.GetRequiredService<IDialogService>(),
            serviceProvider.GetRequiredService<NavigationLockService>(),
            serviceProvider.GetRequiredService<LocalInstanceService>());
    }
}
