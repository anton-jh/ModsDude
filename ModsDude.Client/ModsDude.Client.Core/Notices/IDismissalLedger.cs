namespace ModsDude.Client.Core.Notices;

public interface IDismissalLedger
{
    /// <summary>Raised when something here changed, so the shell can redraw without re-checking.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Whether this notice, saying exactly this, has been waved away.
    /// </summary>
    /// <param name="signature">
    /// What it says now. A dismissal recorded against a different one does not count, which is how a
    /// third stray mod under a dismissed notice brings it straight back.
    /// </param>
    bool IsDismissed(string key, string signature);

    void Dismiss(Notice notice);

    void Dismiss(string key, string signature);

    /// <summary>
    /// Everything currently showing, in one gesture. The column's own button, for the morning after
    /// an update-all that touched every game on the machine.
    /// </summary>
    void DismissAll(IEnumerable<Notice> notices);

    /// <summary>
    /// Forgets every dismissal whose notice is no longer being raised.
    /// </summary>
    /// <remarks>
    /// Called with the keys of a fresh build, before the dismissals are applied to it. A problem that
    /// was waved away and then actually fixed leaves no entry behind, so the same problem occurring
    /// again next week is announced rather than swallowed by a dismissal from a previous evening.
    /// </remarks>
    void Retain(IEnumerable<string> liveKeys);

    /// <summary>Signing out, or switching accounts: none of it is this user's any more.</summary>
    void Clear();
}
