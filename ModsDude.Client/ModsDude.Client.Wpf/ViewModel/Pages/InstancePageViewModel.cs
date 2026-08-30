using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The instance's own shell, with the same sub-navigation a profile has: applying a profile, and the
/// settings that say where the folder is.
/// </summary>
/// <remarks>
/// Sync opens first because it is the reason to come here. Phase 4 grows this into the instance's
/// full page - active profile, drift status and Re-apply beside each other - which is why the
/// sub-navigation exists now rather than the sync page being reached from a button on Manage.
/// See docs/PLAN.md#phase-4--make-drift-unmissable.
/// </remarks>
public class InstancePageViewModel : PageViewModel, IDisposable
{
    public InstancePageViewModel(
        Repo repo,
        LocalInstance instance,
        NavigationManager navigationManager,
        SyncPageViewModel.Factory syncPageViewModelFactory,
        EditLocalInstancePageViewModel.Factory editLocalInstancePageViewModelFactory)
    {
        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Sync", () => syncPageViewModelFactory.Create(repo, instance)),
            new MenuItemViewModel("Manage", () => editLocalInstancePageViewModelFactory.Create(repo, instance))
        ];

        NavManager.Selected = MenuItems.First();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }


    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on.
    /// </summary>
    public void Dispose()
    {
        NavManager.Dispose();
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public InstancePageViewModel Create(Repo repo, LocalInstance instance)
            => ActivatorUtilities.CreateInstance<InstancePageViewModel>(serviceProvider, repo, instance);
    }
}
