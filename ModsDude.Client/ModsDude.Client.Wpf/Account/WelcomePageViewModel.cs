using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Where the app lands when there is no repo to open: a first sign-in, or the last repo gone. Its way on
/// is joining one or creating one - the same two entries the repo list's header offers.
/// </summary>
/// <remarks>
/// The shell's entries rather than pages of its own, so that choosing one here is navigating to it: the
/// sidebar's header draws it selected.
/// </remarks>
public sealed partial class WelcomePageViewModel(
    MenuItemViewModel joinRepo,
    MenuItemViewModel createRepo,
    Action<MenuItemViewModel> open,
    AccountViewModel account,
    TrustCodeFormViewModel trustCode)
    : PageViewModel
{
    public AccountViewModel Account { get; } = account;

    public TrustCodeFormViewModel TrustCode { get; } = trustCode;


    [RelayCommand]
    private void JoinRepo() => open(joinRepo);

    [RelayCommand]
    private void CreateRepo() => open(createRepo);


    public class Factory(IServiceProvider serviceProvider)
    {
        public WelcomePageViewModel Create(MenuItemViewModel joinRepo, MenuItemViewModel createRepo, Action<MenuItemViewModel> open)
            => ActivatorUtilities.CreateInstance<WelcomePageViewModel>(serviceProvider, joinRepo, createRepo, open);
    }
}
