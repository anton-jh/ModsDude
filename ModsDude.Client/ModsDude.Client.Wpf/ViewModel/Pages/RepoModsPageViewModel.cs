using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public class RepoModsPageViewModel : PageViewModel, IDisposable
{
    public RepoModsPageViewModel(
        Repo repo,
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


    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on. Dragging across the menu leaves one of those behind
    /// per page it passes over.
    /// </summary>
    public void Dispose()
    {
        NavManager.Dispose();
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsPageViewModel>(serviceProvider, repo);
    }
}
