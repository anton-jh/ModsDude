using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public class RepoModsPageViewModel : PageViewModel
{
    public RepoModsPageViewModel(
        RepoModel repo,
        RepoModsImportPageViewModel.Factory repoModsImportPageViewModelFactory,
        NavigationManager navigationManager)
    {
        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Import", () => repoModsImportPageViewModelFactory.Create(repo)),
            new MenuItemViewModel("Manage", () => new ExamplePageViewModel(repo.Name, "Mods | Manage"))
            ];
        NavManager.Selected = MenuItems.First();
    }


    public NavigationManager NavManager { get; }
    public ObservableCollection<MenuItemViewModel> MenuItems { get; }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsPageViewModel Create(RepoModel repo)
            => ActivatorUtilities.CreateInstance<RepoModsPageViewModel>(serviceProvider, repo);
    }
}
