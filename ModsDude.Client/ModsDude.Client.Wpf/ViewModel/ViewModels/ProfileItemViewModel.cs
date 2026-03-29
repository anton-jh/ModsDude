using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class ProfileItemViewModel(
    RepoModel repo,
    ProfileDto profile,
    ProfilePageViewModelFactory profilePageViewModelFactory)
    : MenuItemViewModel(
        profile.Name,
        () => profilePageViewModelFactory.Create(repo, profile))
{
    public Guid Id => profile.Id;
}
