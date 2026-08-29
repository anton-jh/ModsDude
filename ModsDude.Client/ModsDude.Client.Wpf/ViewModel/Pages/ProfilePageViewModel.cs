using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public class ProfilePageViewModel : PageViewModel, IDisposable
{
    public ProfilePageViewModel(
        Repo repo,
        ProfileDto profile,
        NavigationManager navigationManager,
        ProfileOverviewPageViewModel.Factory profileOverviewPageViewModelFactory,
        EditProfilePageViewModel.Factory editProfilePageViewModelFactory,
        ProfileModsEditorPageViewModel.Factory profileModsEditorPageViewModelFactory)
    {
        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Overview", () => profileOverviewPageViewModelFactory.Create(repo, profile)),
            new MenuItemViewModel("Mods", () => profileModsEditorPageViewModelFactory.Create(profile)),
            new MenuItemViewModel("Manage", () => editProfilePageViewModelFactory.Create(repo, profile))
        ];

        NavManager.Selected = MenuItems.First();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }


    /// <summary>
    /// Without this the sub page this owns is never disposed, so its initialization keeps running
    /// long after the user has navigated on - the same reason <see cref="RepoModsPageViewModel"/>
    /// is disposable.
    /// </summary>
    public void Dispose()
    {
        NavManager.Dispose();
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfilePageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfilePageViewModel>(serviceProvider, repo, profile);
    }
}
