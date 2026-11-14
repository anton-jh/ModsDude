namespace ModsDude.Client.Wpf.Shell;

/// <summary>
/// Ending the session on purpose - leaving the app or switching user: what it would cost right now,
/// the one question asked before it, and stopping everything.
/// </summary>
public interface ISessionEnd
{
    /// <summary>Work part way through and unsaved changes, named. Empty where nothing would be lost.</summary>
    IReadOnlyList<string> DescribeAtStake();

    /// <summary>
    /// Asks, with the window brought forward, where anything is at stake.
    /// </summary>
    /// <returns>True where nothing is at stake or the user agreed.</returns>
    Task<bool> ConfirmAsync(string title, string confirmText);

    /// <summary>
    /// Ends every open modal unanswered, so work waiting on one can unwind, then stops every piece of
    /// running work and waits for it. Shown in the progress strip while it waits.
    /// </summary>
    /// <returns>False where something was still running when the wait ran out.</returns>
    Task<bool> StopAsync();
}
