using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Repos;

namespace ModsDude.Client.Wpf.Home;

/// <summary>The one next step a repo's row on Home offers, decided by where the repo stands here.</summary>
public enum HomeRepoAction
{
    None,
    ConnectGame,
    Review,
    Apply,
    OpenProfile
}


/// <param name="IsFollowed">Whether the game follows it already, so picking it would change nothing.</param>
public sealed record HomeProfileOption(Guid ProfileId, string Name, bool IsFollowed)
{
    public bool IsPickable => IsFollowed is false;
}


/// <summary>A savegame this machine has checked out from the repo.</summary>
/// <param name="Name">What it is called, or null until that has been read.</param>
public sealed record HomeHeldSavegame(Game Game, Guid SavegameId, string? Name)
{
    public string Title => Name ?? "Savegame";

    /// <summary>A check-in writes the name into the slot, so it waits for the name.</summary>
    public bool CanCheckIn => Name is not null;
}


/// <summary>Everything a repo's row on Home shows, as of one read of the stores it is drawn from.</summary>
/// <param name="Tag">The repo's tag where another repo here has the same name, otherwise null.</param>
/// <param name="FollowedProfileId">The profile of this repo the game follows, or null where it follows none here.</param>
/// <param name="FollowedProfileName">Its name, or null where it is archived or not read yet.</param>
/// <param name="ReviewTarget">The folder that drifted, opened already scanned by Review.</param>
/// <param name="IsBusy">Whether an apply holds the game right now.</param>
/// <param name="CanPickProfile">Whether the game is connected and its adapter has mods to put a profile's on.</param>
/// <param name="ProfilesLoaded">Whether the repo's profiles have been read, so an empty picker means there are none.</param>
/// <param name="Friends">The repo's profiles friends are on, the most recently active first.</param>
public sealed record HomeRepoState(
    Guid RepoId,
    string Name,
    string? Tag,
    RepoMembershipLevel MembershipLevel,
    Guid? FollowedProfileId,
    string? FollowedProfileName,
    int? PinnedRevision,
    ProfileSyncState SyncState,
    ModTargetRef? ReviewTarget,
    HomeRepoAction Action,
    bool IsBusy,
    bool CanPickProfile,
    bool ProfilesLoaded,
    IReadOnlyList<HomeProfileOption> Profiles,
    IReadOnlyList<FriendProfileGroupViewModel> Friends,
    IReadOnlyList<RepoSectionAccess> Shortcuts,
    IReadOnlyList<HomeHeldSavegame> HeldSavegames)
{
    public bool HasTag => Tag is not null;

    public string TagText => $"#{Tag}";

    public string MembershipText => MembershipLevel switch
    {
        RepoMembershipLevel.Admin => "Admin",
        RepoMembershipLevel.Member => "Member",
        _ => "Guest"
    };

    public bool IsFollowing => FollowedProfileId is not null;

    public string PickerLabel => IsFollowing
        ? FollowedProfileName ?? "Archived profile"
        : "Choose profile";

    public bool HasPin => IsFollowing && PinnedRevision is not null;

    public string PinText => $"rev {PinnedRevision}";

    public bool HasSyncState => SyncState is not ProfileSyncState.None;

    public string SyncLabel => ProfileSyncStatusService.Describe(SyncState);

    public bool HasAction => Action is not HomeRepoAction.None;

    public string ActionLabel => Action switch
    {
        HomeRepoAction.ConnectGame => "Connect game",
        HomeRepoAction.Review => "Review",
        HomeRepoAction.Apply => "Apply",
        HomeRepoAction.OpenProfile => "Open profile",
        _ => ""
    };

    public bool IsIdle => IsBusy is false;

    public bool HasProfiles => Profiles.Count > 0;

    public bool HasFriends => Friends.Count > 0;

    public bool HasHeldSavegames => HeldSavegames.Count > 0;
}
