namespace ModsDude.Client.Core.Notices;

/// <summary>
/// What the user has waved away, and what it looked like when they did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per notice, which is the whole reason this exists.</b> Dismissal used to be one signature over
/// every drifted folder and every corrupt blob at once - see the shape <c>DriftMonitor</c> carried
/// before the column - so waving away a folder that gained two stray mods also silenced a locked map
/// in another game and a savegame nobody had checked in. One card had one button, and one button
/// could only mean everything.
/// </para>
/// <para>
/// <b>Still deliberately weak, and for the original reason.</b> Nothing here is persisted, there is
/// no permanent form of it, and a dismissal lasts only while the notice keeps saying the same thing:
/// a warning waved away for good is a savegame silently at risk. See
/// docs/07-mod-sync-design.md#it-has-to-be-unmissable-everywhere.
/// </para>
/// <para>
/// <b>It never grows without bound, and never forgets too early.</b> An entry is dropped when its
/// notice stops being raised - <see cref="Retain"/>, called with the keys the current build produced
/// - rather than on a timer, so a problem that comes back after being genuinely fixed is announced
/// again rather than being silenced by a dismissal nobody remembers making.
/// </para>
/// </remarks>
public sealed class DismissalLedger
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _dismissed = [];


    /// <summary>Raised when something here changed, so the shell can redraw without re-checking.</summary>
    public event EventHandler? Changed;


    /// <summary>
    /// Whether this notice, saying exactly this, has been waved away.
    /// </summary>
    /// <param name="signature">
    /// What it says now. A dismissal recorded against a different one does not count, which is how a
    /// third stray mod under a dismissed notice brings it straight back.
    /// </param>
    public bool IsDismissed(string key, string signature)
    {
        lock (_lock)
        {
            return _dismissed.TryGetValue(key, out var dismissed) && dismissed == signature;
        }
    }

    public void Dismiss(Notice notice) => Dismiss(notice.Key, notice.Signature);

    public void Dismiss(string key, string signature)
    {
        lock (_lock)
        {
            _dismissed[key] = signature;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Everything currently showing, in one gesture. The column's own button, for the morning after
    /// an update-all that touched every game on the machine.
    /// </summary>
    public void DismissAll(IEnumerable<Notice> notices)
    {
        lock (_lock)
        {
            foreach (var notice in notices.Where(x => x.CanDismiss))
            {
                _dismissed[notice.Key] = notice.Signature;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets every dismissal whose notice is no longer being raised.
    /// </summary>
    /// <remarks>
    /// Called with the keys of a fresh build, before the dismissals are applied to it. A problem that
    /// was waved away and then actually fixed leaves no entry behind, so the same problem occurring
    /// again next week is announced rather than swallowed by a dismissal from a previous evening.
    /// </remarks>
    public void Retain(IEnumerable<string> liveKeys)
    {
        var live = liveKeys as IReadOnlySet<string> ?? liveKeys.ToHashSet(StringComparer.Ordinal);

        lock (_lock)
        {
            foreach (var stale in _dismissed.Keys.Where(x => live.Contains(x) is false).ToList())
            {
                _dismissed.Remove(stale);
            }
        }
    }

    /// <summary>Signing out, or switching accounts: none of it is this user's any more.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _dismissed.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
