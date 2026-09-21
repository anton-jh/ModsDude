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
/// </remarks>
public sealed class DriftToastPlanner
{
    /// <summary>Where a long body is cut. A toast is glanced at, and Windows clips it anyway.</summary>
    public const int MaxBodyLength = 180;

    private const int MaxListed = 3;
    private const string UnreachablePrefix = "unreachable/";

    private HashSet<string> _known = [];


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
    public DriftToast? Observe(IReadOnlyList<Notice> live, bool reposLoaded, bool windowInFront)
    {
        var eligible = live.Where(x => IsEligible(x, reposLoaded)).ToList();

        var fresh = eligible.Any(x => _known.Contains(x.Key) is false);

        // Replaced rather than added to: a key that left has to be able to be news again.
        _known = [.. eligible.Select(x => x.Key)];

        if (fresh is false || windowInFront)
        {
            return null;
        }

        return Describe(eligible);
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
