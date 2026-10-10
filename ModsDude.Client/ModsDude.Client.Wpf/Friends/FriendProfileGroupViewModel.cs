using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Savegames;

namespace ModsDude.Client.Wpf.Friends;

/// <param name="Name">The display name, with the tag where two friends in the list share it.</param>
public sealed record FriendAvatar(AvatarViewModel Avatar, string Name);


/// <summary>
/// Friends on the same mod list as a list draws them: the profile, who is on it, when it was last
/// active, and the button that puts this machine there too.
/// </summary>
public sealed partial class FriendProfileGroupViewModel
{
    private readonly FriendActivityListViewModel _owner;


    public FriendProfileGroupViewModel(
        FriendActivityListViewModel owner,
        FriendProfileGroup group,
        IReadOnlyList<FriendAvatar> friends,
        IFriendActivityEnvironment environment,
        bool isFollowing,
        DateTimeOffset now)
    {
        _owner = owner;

        var availability = environment.CanFollow(group.Latest);

        Model = group;
        Friends = friends;
        IsPlaying = group.IsPlaying(now.UtcDateTime);
        LastActive = $"Active {SavegameWording.Ago(group.LatestAt, now)}";
        IsFollowing = isFollowing;
        CanFollow = availability is FollowAvailability.Available;
        FollowLabel = FriendActivityRules.FollowLabel(group.Latest);
        FollowBlocked = FriendActivityRules.FollowBlocked(group.Latest, availability, environment);
    }


    public FriendProfileGroup Model { get; }

    public string ProfileName => Model.ProfileName;

    public bool HasRevision => Model.PinnedRevision is not null;

    public string RevisionText => $"rev {Model.PinnedRevision}";

    public IReadOnlyList<FriendAvatar> Friends { get; }

    public bool IsPlaying { get; }

    public string LastActive { get; }

    public bool CanFollow { get; }

    public string FollowLabel { get; }

    /// <summary>Why there is no follow button, where there is not.</summary>
    public string? FollowBlocked { get; }

    public bool HasFollowBlocked => FollowBlocked is not null;

    /// <summary>Whether a follow started from this list is still going, which holds the button off.</summary>
    public bool IsFollowing { get; }

    public bool IsIdle => IsFollowing is false;


    [RelayCommand]
    private Task Follow() => _owner.FollowAsync(this);
}
