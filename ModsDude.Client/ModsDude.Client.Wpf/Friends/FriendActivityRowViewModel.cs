using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Savegames;

namespace ModsDude.Client.Wpf.Friends;

/// <summary>
/// One friend's game as a list draws it: who, which profile, what they last did with it, and the
/// button that puts this machine on the same thing.
/// </summary>
public sealed class FriendActivityRowViewModel
{
    public FriendActivityRowViewModel(
        GameActivityDto activity,
        IFriendActivityEnvironment environment,
        AvatarViewModel avatar,
        bool showTag,
        bool showWhere,
        bool isFollowing,
        DateTimeOffset now)
    {
        Model = activity;

        var availability = environment.CanFollow(activity);

        Name = activity.User.DisplayName;
        Tag = showTag ? $"#{activity.User.Tag}" : null;
        Avatar = avatar;

        Profile = activity.ProfileName;
        IsPlaying = FriendActivityRules.IsPlaying(activity, now.UtcDateTime);
        Where = showWhere
            ? environment.DescribeRepo(activity.RepoId) is string repo
                ? $"{environment.DescribeGame(activity.Game)} · {repo}"
                : environment.DescribeGame(activity.Game)
            : null;
        ProfileDetail = activity.PinnedRevision is int pinned ? $"rev {pinned}" : "";

        var what = activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $"Checked out {(activity.SavegameName is string save ? $"'{save}'" : "a savegame")} {SavegameWording.Ago(activity.ChangedAt, now)}"
            : $"Switched to it {SavegameWording.Ago(activity.ChangedAt, now)}";

        // Only where it says something the first half did not: a re-apply since is somebody still
        // playing on it, and "switched 3 days ago" alone would read as somebody who has stopped.
        Summary = activity.TouchedAt - activity.ChangedAt > TimeSpan.FromMinutes(5)
            ? $"{what} · active {SavegameWording.Ago(activity.TouchedAt, now)}"
            : what;

        IsFollowing = isFollowing;
        CanFollow = availability is FollowAvailability.Available;
        FollowLabel = FriendActivityRules.FollowLabel(activity);
        FollowBlocked = FriendActivityRules.FollowBlocked(activity, availability, environment);
    }


    public GameActivityDto Model { get; }

    public string Name { get; }

    /// <summary>The four digits, only where two friends in this list share a name.</summary>
    public string? Tag { get; }

    public bool HasTag => Tag is not null;

    public AvatarViewModel Avatar { get; }

    public string Profile { get; }

    public string ProfileLine => $"on '{Profile}'";

    public bool IsPlaying { get; }

    /// <summary>The game and the repo, where the list spans repos; null on one repo's list.</summary>
    public string? Where { get; }

    public bool HasWhere => Where is not null;

    /// <summary>
    /// The revision where they are held on one. Head goes unsaid.
    /// </summary>
    public string ProfileDetail { get; }

    public bool HasProfileDetail => ProfileDetail.Length > 0;

    /// <summary>What they last did with it, and when.</summary>
    public string Summary { get; }

    public bool CanFollow { get; }
    public string FollowLabel { get; }

    /// <summary>Why there is no button, where there is not.</summary>
    public string? FollowBlocked { get; }

    public bool HasFollowBlocked => FollowBlocked is not null;

    /// <summary>Whether a follow started from this list is still going, which holds the button off.</summary>
    public bool IsFollowing { get; }

    public bool IsIdle => IsFollowing is false;
}


