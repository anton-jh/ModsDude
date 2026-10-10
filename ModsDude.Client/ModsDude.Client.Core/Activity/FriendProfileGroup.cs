using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Activity;

/// <summary>
/// Friends on the same mod list: one profile of one repo, at head or held on the same revision. One
/// follow puts this machine where every one of them is.
/// </summary>
/// <param name="Activities">One per friend, the most recently active first.</param>
public sealed record FriendProfileGroup(
    Guid RepoId,
    Guid ProfileId,
    int? PinnedRevision,
    IReadOnlyList<GameActivityDto> Activities)
{
    /// <summary>The most recent activity, which also names the profile and is what a follow follows.</summary>
    public GameActivityDto Latest => Activities[0];

    public string ProfileName => Latest.ProfileName;

    public DateTime LatestAt => Latest.TouchedAt;

    public bool IsPlaying(DateTime now) => Activities.Any(x => FriendActivityRules.IsPlaying(x, now));


    /// <summary>The groups the activities make, the one with the most recent activity first.</summary>
    /// <remarks>A friend listed twice in a group, through two installations of its game, is shown once.</remarks>
    public static IReadOnlyList<FriendProfileGroup> Build(IEnumerable<GameActivityDto> activities)
        => [.. activities
            .GroupBy(x => (x.RepoId, x.ProfileId, x.PinnedRevision))
            .Select(x => new FriendProfileGroup(
                x.Key.RepoId,
                x.Key.ProfileId,
                x.Key.PinnedRevision,
                [.. x
                    .OrderByDescending(activity => activity.TouchedAt)
                    .ThenBy(activity => activity.User.Id, StringComparer.Ordinal)
                    .ThenBy(activity => activity.Game, StringComparer.Ordinal)
                    .DistinctBy(activity => activity.User.Id)]))
            .OrderByDescending(x => x.LatestAt)
            .ThenBy(x => x.RepoId)
            .ThenBy(x => x.ProfileId)
            .ThenBy(x => x.PinnedRevision ?? -1)];
}
