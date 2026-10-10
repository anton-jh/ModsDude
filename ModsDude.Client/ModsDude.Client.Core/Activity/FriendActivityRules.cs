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
/// repo's overview, the notice column and the Windows toasts.
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

    /// <summary>
    /// Whether the friend's game is being played, by the server's last heartbeat and this machine's clock.
    /// </summary>
    public static bool IsPlaying(GameActivityDto activity, DateTime now)
        => activity.PlayingUntil is DateTime until && now < until;

    /// <summary>
    /// Whether a row says something that happened after <paramref name="since"/>: a switch or a
    /// check-out, or a play session that started since and is still going.
    /// </summary>
    /// <remarks>
    /// A session that has already ended is not news: whoever played it is not there to be joined.
    /// </remarks>
    public static bool IsNews(GameActivityDto activity, DateTime since, DateTime now)
        => activity.ChangedAt > since
            || (IsPlaying(activity, now) && activity.PlayingSince > since);

    /// <summary>The newest thing a row says happened, which is how far the news has been told once it is.</summary>
    public static DateTime NewsAt(GameActivityDto activity)
        => activity.PlayingSince is DateTime since && since > activity.ChangedAt ? since : activity.ChangedAt;

    /// <summary>The news in a row, or null where it has none.</summary>
    public static FriendNews? ToNews(GameActivityDto activity, DateTime since, DateTime now)
        => IsNews(activity, since, now) ? new FriendNews(activity, IsPlaying(activity, now)) : null;

    /// <summary>
    /// Whether news is worth a card or a toast.
    /// </summary>
    /// <remarks>
    /// <b>A friend switching onto what this game is already on tells nobody anything.</b> They have come
    /// to where this user already is, and there is nothing to follow. Measured the way
    /// <see cref="CanFollow"/> measures it, so a friend held on a revision this game is not on still
    /// counts. A check-out is always news: it names the savegame they are playing, which the profile
    /// alone does not. So is starting to play: somebody already on the same profile is exactly who can
    /// join them at once. A repo's overview still lists the friend - this is only about announcing.
    /// </remarks>
    public static bool IsWorthAnnouncing(FriendNews news, IFriendActivityEnvironment environment)
        => news.IsPlaying
            || news.Activity.Kind is GameActivityKind.SavegameCheckedOut
            || environment.CanFollow(news.Activity) is not FollowAvailability.AlreadyOn;

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
    public static string Headline(FriendNews news)
    {
        var activity = news.Activity;

        return news.IsPlaying
            ? $"{activity.User.DisplayName} is playing"
            : activity.Kind is GameActivityKind.SavegameCheckedOut
                ? $"{activity.User.DisplayName} checked out {Quote(activity.SavegameName) ?? "a savegame"}"
                : $"{activity.User.DisplayName} switched to '{activity.ProfileName}'";
    }

    /// <summary>Which mod list that puts them on, and where - one line under the headline.</summary>
    public static string Describe(FriendNews news, IFriendActivityEnvironment environment)
    {
        var activity = news.Activity;
        var game = environment.DescribeGame(activity.Game);
        var repo = environment.DescribeRepo(activity.RepoId) is string name ? $" in {name}" : "";
        var revision = activity.PinnedRevision is int pinned ? $" rev {pinned}" : "";
        var savegame = activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $", {Quote(activity.SavegameName) ?? "a savegame"}"
            : "";

        if (news.IsPlaying)
        {
            return $"{game}{savegame}, on '{activity.ProfileName}'{revision}{repo}.";
        }

        return activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $"{game}, on '{activity.ProfileName}'{revision}{repo}."
            : $"{game}{repo}.{(revision.Length > 0 ? $" Held on{revision}." : "")}";
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
    /// <b>Keyed on the friend and the game, signed with what happened when.</b> A second switch, or
    /// starting to play, replaces the first card rather than stacking under it - what they are doing
    /// now is the news - and brings it back even where the first one was dismissed. A playing card
    /// goes once they stop, leaving the switch before it where that is still news.
    /// </para>
    /// <para>
    /// Info, because nothing here is wrong: it is somebody else's evening, offered in case this user
    /// wants to join it.
    /// </para>
    /// <para>
    /// Only what <see cref="IsWorthAnnouncing"/> passes. Asked on every build, so a card held back
    /// because this game was already there comes up once it has moved elsewhere - at which point the
    /// friend is somewhere this user is not, and the card has a follow button to offer.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Notice> BuildNotices(IEnumerable<FriendNews> news, IFriendActivityEnvironment environment)
    {
        var notices = new List<Notice>();

        foreach (var item in news.OrderByDescending(x => NewsAt(x.Activity)).ThenBy(x => NoticeKey(x.Activity), StringComparer.Ordinal))
        {
            if (IsWorthAnnouncing(item, environment) is false)
            {
                continue;
            }

            var activity = item.Activity;
            var availability = environment.CanFollow(activity);
            var game = ParseGame(activity.Game);
            var happened = item.IsPlaying
                ? $"playing/{activity.PlayingSince?.Ticks}"
                : $"{activity.ChangedAt.Ticks}/{activity.Kind}";

            notices.Add(new Notice(
                NoticeKey(activity),
                $"{happened}/{activity.ProfileId}/{activity.PinnedRevision}",
                NoticeSeverity.Info,
                Headline(item))
            {
                Body = Describe(item, environment),
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
