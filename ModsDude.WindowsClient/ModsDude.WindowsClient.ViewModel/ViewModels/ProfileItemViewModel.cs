using ModsDude.WindowsClient.ApiClient.Generated;
using ModsDude.WindowsClient.ViewModel.Pages;

namespace ModsDude.WindowsClient.ViewModel.ViewModels;
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
