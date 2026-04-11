using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class ProfileItemViewModel(
    Repo repo,
    ProfileDto profile,
    ProfilePageViewModel.Factory profilePageViewModelFactory)
    : MenuItemViewModel(
        profile.Name,
        () => profilePageViewModelFactory.Create(repo, profile))
{
    public Guid Id => profile.Id;
}
