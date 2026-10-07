using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// Follows the profile's name rather than snapshotting it, the way the repo entries follow theirs.
/// </summary>
public class ProfileItemViewModel
    : MenuItemViewModel
{
    private readonly Profile _profile;


    public ProfileItemViewModel(
        Repo repo,
        Profile profile,
        ProfilePageViewModel.Factory profilePageViewModelFactory)
        : base(
            profile.Name,
            () => profilePageViewModelFactory.Create(repo, profile),
            profile,
            () => profile.Name,
            nameof(Profile.Name))
    {
        _profile = profile;

        Icon = MenuIcons.Profile;
    }


    public Guid Id => _profile.Id;

    public override bool IsEntity => true;
}
