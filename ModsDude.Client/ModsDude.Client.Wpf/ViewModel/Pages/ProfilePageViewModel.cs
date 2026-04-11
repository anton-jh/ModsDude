using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public class ProfilePageViewModel : PageViewModel
{
    public ProfilePageViewModel(
        Repo repo,
        ProfileDto profile,
        NavigationManager navigationManager,
        EditProfilePageViewModel.Factory editProfilePageViewModelFactory,
        ProfileModsEditorPageViewModel.Factory profileModsEditorPageViewModelFactory)
    {
        NavManager = navigationManager;
        MenuItems = [
            new MenuItemViewModel("Overview", () => new ExamplePageViewModel(profile.Name, "Overview")),
            new MenuItemViewModel("Mods", () => profileModsEditorPageViewModelFactory.Create(profile)),
            new MenuItemViewModel("Manage", () => editProfilePageViewModelFactory.Create(repo, profile))
        ];

        NavManager.Selected = MenuItems.First();
    }


    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public NavigationManager NavManager { get; }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfilePageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfilePageViewModel>(serviceProvider, repo, profile);
    }
}
