using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Activity;

/// <summary>Whether this machine can be put on what a friend is on.</summary>
public enum FollowAvailability
{
    /// <summary>It can, and it is not there already.</summary>
    Available,

    /// <summary>The game here is already on that profile, at that revision.</summary>
    AlreadyOn,

    /// <summary>The friend's game is not connected on this machine, so there is nothing to put on it.</summary>
    NotConnected
}


/// <summary>What the rows and notices about friends need to know about this machine.</summary>
public interface IFriendActivityEnvironment
{
    /// <summary>What the game is called, from a repo offering it or the game connected here.</summary>
    string DescribeGame(string game);

    /// <summary>The repo's name as the user's lists draw it, or null where it is not loaded.</summary>
    string? DescribeRepo(Guid repoId);

    FollowAvailability CanFollow(GameActivityDto activity);
}


/// <summary>
/// The rules for drawing a friend's game and for following them onto it, in one place for Home, a
/// repo's overview and the notice column.
/// </summary>
public static class FriendActivityRules
{
    /// <summary>What every notice about a friend is grouped under.</summary>
    public const string GroupLabel = "Friends";

    private const string _keyPrefix = "friend/";


    /// <summary>
    /// Whether following this friend would change anything here.
    /// </summary>
    /// <remarks>
    /// <b>Already on it means the same profile at the same revision.</b> A friend on head and this game
    /// held on a past revision of the same profile are on different mod lists, and so are the reverse -
    /// which is exactly when following them is worth a button.
    /// </remarks>
    /// <param name="game">The game connected here for the friend's game, or null where there is none.</param>
    /// <param name="requiredRevision">
    /// The revision this game has to be on for the friend's profile, from a held savegame or its own
    /// pin, or null where it follows head.
    /// </param>
    public static FollowAvailability CanFollow(Game? game, int? requiredRevision, GameActivityDto activity)
    {
        if (game is null)
        {
            return FollowAvailability.NotConnected;
        }

        return game.ActiveProfile == new ActiveProfile(activity.RepoId, activity.ProfileId)
            && requiredRevision == activity.PinnedRevision
            ? FollowAvailability.AlreadyOn
            : FollowAvailability.Available;
    }

    /// <summary>The game identity as the client parses it, or null where the report named something unreadable.</summary>
    public static GameIdentity? ParseGame(string game)
    {
        try
        {
            return GameIdentity.Parse(game);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What a friend did, as a sentence starting with their name.</summary>
    public static string Headline(GameActivityDto activity)
        => activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $"{activity.User.DisplayName} checked out {Quote(activity.SavegameName) ?? "a savegame"}"
            : $"{activity.User.DisplayName} switched to '{activity.ProfileName}'";

    /// <summary>Which mod list that puts them on, and where - one line under the headline.</summary>
    public static string Describe(GameActivityDto activity, IFriendActivityEnvironment environment)
    {
        var repo = environment.DescribeRepo(activity.RepoId) is string name ? $" in {name}" : "";
        var revision = activity.PinnedRevision is int pinned ? $" rev {pinned}" : "";

        return activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $"{environment.DescribeGame(activity.Game)}, on '{activity.ProfileName}'{revision}{repo}."
            : $"{environment.DescribeGame(activity.Game)}{repo}.{(revision.Length > 0 ? $" Held on{revision}." : "")}";
    }

    /// <summary>
    /// What the follow button says. It names the revision only where the friend is held on one:
    /// following an ordinary activation follows the profile, not whatever number head happened to be.
    /// </summary>
    public static string FollowLabel(GameActivityDto activity)
        => activity.PinnedRevision is int pinned ? $"Use rev {pinned}" : "Use this profile";

    /// <summary>Why there is no follow button, or null where there is one.</summary>
    public static string? FollowBlocked(GameActivityDto activity, FollowAvailability availability, IFriendActivityEnvironment environment)
        => availability switch
        {
            FollowAvailability.AlreadyOn => "You are on this too.",
            FollowAvailability.NotConnected => $"{environment.DescribeGame(activity.Game)} is not connected on this machine.",
            _ => null
        };

    /// <summary>The one card per friend per game, whatever they last did on it.</summary>
    public static string NoticeKey(GameActivityDto activity) => $"{_keyPrefix}{activity.User.Id}/{activity.Game}";

    /// <summary>Whether a notice key is one of these, for the column to route its action.</summary>
    public static bool Owns(string key) => key.StartsWith(_keyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// One card per friend per game that changed this session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keyed on the friend and the game, signed with when it changed.</b> A second switch replaces
    /// the first card rather than stacking under it - what they are on now is the news - and brings
    /// it back even where the first one was dismissed.
    /// </para>
    /// <para>
    /// Info, because nothing here is wrong: it is somebody else's evening, offered in case this user
    /// wants to join it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Notice> BuildNotices(IEnumerable<GameActivityDto> news, IFriendActivityEnvironment environment)
    {
        var notices = new List<Notice>();

        foreach (var activity in news.OrderByDescending(x => x.ChangedAt))
        {
            var availability = environment.CanFollow(activity);
            var game = ParseGame(activity.Game);

            notices.Add(new Notice(
                NoticeKey(activity),
                $"{activity.ChangedAt.Ticks}/{activity.Kind}/{activity.ProfileId}/{activity.PinnedRevision}",
                NoticeSeverity.Info,
                Headline(activity))
            {
                Body = Describe(activity, environment),
                Footnote = FollowBlocked(activity, availability, environment),
                Actions = availability is FollowAvailability.Available
                    ? [new NoticeAction(NoticeActionKind.UseProfile, FollowLabel(activity)) { IsPrimary = true }]
                    : [],
                GroupLabel = GroupLabel,
                Subject = game is GameIdentity identity
                    ? new NoticeSubject(identity, environment.DescribeGame(activity.Game))
                    {
                        RepoId = activity.RepoId,
                        ProfileId = activity.ProfileId,
                        ProfileName = activity.ProfileName,
                        SavegameId = activity.SavegameId,
                        Revision = activity.PinnedRevision
                    }
                    : null
            });
        }

        return notices;
    }


    private static string? Quote(string? text) => text is null ? null : $"'{text}'";
}
