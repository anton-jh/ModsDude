using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class EditLocalInstancePageViewModelFactory(
    IServiceProvider serviceProvider)
{
    public EditLocalInstancePageViewModel Create(RepoModel repo, LocalInstance subject)
    {
        return new(
            repo,
            subject,
            serviceProvider.GetRequiredService<IGameAdapterIndex>(),
            serviceProvider.GetRequiredService<IDialogService>(),
            serviceProvider.GetRequiredService<IModalService>(),
            serviceProvider.GetRequiredService<NavigationLockService>(),
            serviceProvider.GetRequiredService<LocalInstanceService>());
    }
}
