using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class ProfileModsEditorPageViewModel(
    ProfileDto profile)
    : PageViewModel
{
    public string Name { get; } = profile.Name;

    public List<string> Items { get; } = ["test 1", "test 2", "test 3", "test 4"];


    [RelayCommand]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {

    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfileModsEditorPageViewModel Create(ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfileModsEditorPageViewModel>(serviceProvider, profile);
    }
}
