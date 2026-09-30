using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Where the app lands when there is no repo to open: a first sign-in, or the last repo gone. Its way on
/// is joining one or creating one - the same two entries the repo list's header offers.
/// </summary>
/// <remarks>
/// The shell's entries rather than pages of its own, so that choosing one here is navigating to it: the
/// sidebar's header draws it selected, and the trust rule on Create repo is the one the shell applies.
/// </remarks>
public sealed partial class WelcomePageViewModel(
    MenuItemViewModel joinRepo,
    MenuItemViewModel createRepo,
    Action<MenuItemViewModel> open)
    : PageViewModel
{
    /// <summary>For the button's availability and, where it is closed, the reason.</summary>
    public MenuItemViewModel CreateRepoItem { get; } = createRepo;


    [RelayCommand]
    private void JoinRepo() => open(joinRepo);

    [RelayCommand]
    private void CreateRepo() => open(CreateRepoItem);
}
