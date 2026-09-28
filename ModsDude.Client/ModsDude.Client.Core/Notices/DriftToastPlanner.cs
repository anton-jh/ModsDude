using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Notices;

/// <summary>
/// What one Windows toast should say about the notices that are up.
/// </summary>
/// <param name="Title">The headline of the notice, or a count where there are several.</param>
/// <param name="Body">One line per notice, or the notice's own first sentence where there is one.</param>
/// <param name="NoticeKey">
/// The notice a click should take the user to, where the toast is about exactly one. Null for a toast
/// that summarises several: there is no single place to send somebody, so it opens the window and lets
/// the column say the rest.
/// </param>
public sealed record DriftToast(string Title, string Body, string? NoticeKey);


/// <summary>
/// Decides when the notices in the column are worth a Windows toast, and what it says.
/// </summary>
/// <remarks>
/// <para>
/// <b>A toast is for something new that the user is not looking at.</b> The column is where drift is
/// unmissable; a toast is for the moment it is not on screen at all - the window is in the tray, or
/// behind the game. So nothing here fires while the window is in front, and a notice that appeared
/// while it was is <em>seen</em>: leaving the window later does not turn it into news.
/// </para>
/// <para>
/// <b>Only Critical and Warning.</b> A Pending notice is work that is owed rather than something wrong,
/// and Info is an absorbed failure with no consequence worth interrupting for. Both stay in the column.
/// </para>
/// <para>
/// <b>New means a new key, not a new sentence.</b> A notice's text carries counts that move every time
/// the game touches another file, and a toast per change would turn one update-all into a burst.
/// The key is stable for as long as the problem is the same problem, so it is announced once. It is
/// announced again only after it has gone away and come back, which is a different evening.
/// </para>
/// <para>
/// <b>One toast, and it lists everything eligible</b>, not only what is new. A later toast replaces an
/// earlier one in Action Center, so a toast naming only the newcomer would quietly remove the ones
/// before it while their problems are still there.
/// </para>
/// <para>
/// <b>Not stateless on purpose</b>, and the state is the set of keys already announced or seen. It is
/// in memory only: an unresolved problem is mentioned once per run of the app, which after a start at
/// sign-in is the reminder the user would want.
/// </para>
/// <para>
/// <b>Play in progress is held back.</b> A savegame being played gains unchecked-in play the first
/// time the game saves, and a toast saying so would land on top of the game in the middle of the
/// evening it describes. It is neither announced nor counted as seen while that game runs; the
/// reminder when the game closes - see <see cref="Remind"/> - says it instead.
/// </para>
/// </remarks>
public sealed class DriftToastPlanner
{
    /// <summary>Where a long body is cut. A toast is glanced at, and Windows clips it anyway.</summary>
    public const int MaxBodyLength = 180;

    private const int MaxListed = 3;
    private const string UnreachablePrefix = "unreachable/";

    private HashSet<string> _known = [];

    /// <summary>Notices a reminder has already spoken for, before they are up to be seen.</summary>
    private readonly HashSet<string> _reminded = [];


    /// <summary>
    /// Looks at what is up now and returns the toast it warrants, if any.
    /// </summary>
    /// <param name="live">Every notice currently in the column, after dismissals.</param>
    /// <param name="reposLoaded">
    /// Whether the repo list has been read. Until it has, a drifted game reads as belonging to a repo
    /// nobody can see, and toasting "checking which repo it belongs to" would be announcing the app's
    /// own start-up.
    /// </param>
    /// <param name="windowInFront">Whether the user is looking at the window right now.</param>
    /// <param name="holdBack">
    /// Notices to say nothing about yet, and not to count as seen either - see
    /// <see cref="IsPlayInProgress"/>. Once this stops holding one back it is news like any other.
    /// </param>
    public DriftToast? Observe(
        IReadOnlyList<Notice> live,
        bool reposLoaded,
        bool windowInFront,
        Func<Notice, bool>? holdBack = null)
    {
        var eligible = live
            .Where(x => IsEligible(x, reposLoaded))
            .Where(x => holdBack?.Invoke(x) is not true)
            .ToList();

        var fresh = eligible.Any(x => _known.Contains(x.Key) is false && _reminded.Contains(x.Key) is false);

        // Replaced rather than added to: a key that left has to be able to be news again.
        _known = [.. eligible.Select(x => x.Key)];

        // A reminder's notice is known from the first time it is actually up, and like any other
        // from then on.
        _reminded.ExceptWith(_known);

        if (fresh is false || windowInFront)
        {
            return null;
        }

        return Describe(eligible);
    }

    /// <summary>
    /// The reminder for a checked-out savegame played in a session that has just ended.
    /// </summary>
    /// <remarks>
    /// Its notice counts as announced from here on, so the drift check that follows the game closing
    /// does not say the same thing a second time in the digest - even where that check is the first
    /// to find the notice at all, because the game's last save was written as it closed.
    /// </remarks>
    public DriftToast Remind(PlayedSavegame played)
    {
        _reminded.Add(played.NoticeKey);

        var save = played.SlotDisplayName is { Length: > 0 } name ? $"'{name}'" : "the savegame you have checked out";

        return new DriftToast(
            $"Check in {save}",
            $"You played {save} in {played.GameName}. Until it is checked in, that play exists only on this machine.",
            played.NoticeKey);
    }

    /// <summary>
    /// Whether a notice says nothing but that a savegame is being played in a game that is running
    /// right now - the one thing worth holding back until it closes.
    /// </summary>
    /// <remarks>
    /// <b>Nothing but.</b> A save taken over by somebody else, or sitting on the wrong mod list, is
    /// worth hearing about mid-game - the second is exactly what damages it - so a notice saying
    /// either of those as well is not held back.
    /// </remarks>
    public static bool IsPlayInProgress(
        Notice notice,
        IReadOnlyList<TargetDrift> drifted,
        Func<GameIdentity, bool> isRunning)
    {
        if (notice.Subject is not { SavegameId: Guid savegameId } subject
            || notice.Key != NoticeBuilder.SavegameKey(savegameId)
            || isRunning(subject.Game) is false)
        {
            return false;
        }

        var kinds = drifted
            .SelectMany(x => x.Report.SavegameDrift)
            .Where(x => x.SavegameId == savegameId)
            .ToList();

        return kinds.Count > 0 && kinds.All(x => x.Kind is SavegameDriftKind.UncheckedInPlay);
    }


    private static bool IsEligible(Notice notice, bool reposLoaded)
    {
        if (notice.Severity is not (NoticeSeverity.Critical or NoticeSeverity.Warning))
        {
            return false;
        }

        return reposLoaded || notice.Key.StartsWith(UnreachablePrefix, StringComparison.Ordinal) is false;
    }

    private static DriftToast Describe(IReadOnlyList<Notice> notices)
    {
        // Worst first, so the one line a truncated toast keeps is the one that matters.
        var ordered = notices.OrderBy(x => x.Severity).ToList();

        if (ordered.Count == 1)
        {
            var only = ordered[0];

            return new DriftToast(only.Headline, Shorten(only.Body ?? ""), only.Key);
        }

        var lines = ordered.Take(MaxListed).Select(x => x.Headline).ToList();

        if (ordered.Count > MaxListed)
        {
            lines.Add($"and {ordered.Count - MaxListed} more");
        }

        return new DriftToast($"{ordered.Count} things need attention", string.Join('\n', lines), null);
    }

    /// <summary>The first sentence, or as much of the text as fits, ending on a word.</summary>
    private static string Shorten(string text)
    {
        text = text.Trim();

        var sentence = text.IndexOf(". ", StringComparison.Ordinal);
        var first = sentence > 0 ? text[..(sentence + 1)] : text;

        if (first.Length <= MaxBodyLength)
        {
            return first;
        }

        var cut = first.LastIndexOf(' ', MaxBodyLength - 1);

        return $"{first[..(cut > 0 ? cut : MaxBodyLength - 1)].TrimEnd(',', ';', ' ')}...";
    }
}
