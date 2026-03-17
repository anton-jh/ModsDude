using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class ProfileItemViewModel(
    ProfileDto profile,
    ProfilePageViewModelFactory profilePageViewModelFactory)
    : IMenuItemViewModel
{
    public Guid Id => profile.Id;
    public string Title => profile.Name;

    public PageViewModel GetPage()
    {
        return profilePageViewModelFactory.Create(profile);
    }
}
