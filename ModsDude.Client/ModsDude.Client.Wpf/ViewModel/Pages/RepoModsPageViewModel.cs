using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public class RepoModsPageViewModel : PageViewModel, IDisposable
{
    private readonly ModCatalog _catalog;


    public RepoModsPageViewModel(
        Repo repo,
        ModCatalog.Factory catalogFactory,
        RepoModsImportPageViewModel.Factory repoModsImportPageViewModelFactory,
        NavigationManager navigationManager)
    {
        // Owned by the shell rather than by either sub page, so that moving between Import and
        // Manage - which show the same mods under different rules - composes from the scans already
        // in memory instead of walking every mod folder again.
        _catalog = catalogFactory.Create(repo);

        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Import", () => repoModsImportPageViewModelFactory.Create(repo, _catalog)),
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
        _catalog.Dispose();
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsPageViewModel>(serviceProvider, repo);
    }
}
