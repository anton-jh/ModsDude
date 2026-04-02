using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

public class ProfileModsEditorPageViewModelFactory(
    IServiceProvider serviceProvider)
{
    public ProfileModsEditorPageViewModel Create(ProfileDto profile)
    {
        return new(profile);
    }
}
