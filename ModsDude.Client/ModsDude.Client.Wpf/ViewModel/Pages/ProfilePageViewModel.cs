using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class ProfilePageViewModel
    : PageViewModel
{
    [ObservableProperty]
    private string _name;


    public ProfilePageViewModel(ProfileDto profile)
    {
        Name = profile.Name;
    }
}
