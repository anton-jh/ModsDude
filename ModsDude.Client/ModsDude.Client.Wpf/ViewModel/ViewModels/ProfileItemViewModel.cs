using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class ProfileItemViewModel(
    ProfileDto profile)
    : IMenuItemViewModel
{
    public Guid Id => profile.Id;
    public string Title => profile.Name;

    public PageViewModel GetPage()
    {
        return new ExamplePageViewModel($"Manage profile ({profile.Name})");
    }
}
