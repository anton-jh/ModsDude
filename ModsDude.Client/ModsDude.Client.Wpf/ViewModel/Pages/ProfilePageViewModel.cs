using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public class ProfilePageViewModel : PageViewModel
{
    public ProfilePageViewModel(
        RepoModel repo,
        ProfileDto profile,
        NavigationManager navigationManager,
        EditProfilePageViewModelFactory editProfilePageViewModelFactory)
    {
        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Overview", () => new ExamplePageViewModel(profile.Name, "Overview")),
            new MenuItemViewModel("Mods", () => new ExamplePageViewModel(profile.Name, "Mods")),
            new MenuItemViewModel("Manage", () => editProfilePageViewModelFactory.Create(repo, profile))
        ];

        NavManager.Selected = MenuItems.First();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }
}
