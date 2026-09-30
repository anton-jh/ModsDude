
namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// The one way a failure reaches the user, and the reason every failure the user is told about is
/// also in the log.
/// </summary>
/// <remarks>
/// <para>
/// Showing and logging used to be separate acts, which meant they could disagree - and they did.
/// Half a dozen catch blocks built an error modal and nothing else, so the failures a user was most
/// likely to ask about were the ones with no record behind them. Going through here makes the log
/// line a by-product of the modal rather than a second thing somebody has to remember.
/// </para>
/// <para>
/// <b>Not for refusals or validation.</b> A refused delete and a form that is not filled in are
/// deliberate answers, not faults; they keep the confirmation modal and write nothing.
/// </para>
/// </remarks>
public interface IErrorReporter
{
    /// <summary>
    /// Writes the failure to the log and returns the modal for it, stamped with the moment it was
    /// written. For a caller that has to decide when - or whether - to show it.
    /// </summary>
    /// <param name="context">
    /// What the app was doing, in a few words. It is the log's only clue about which of a page's
    /// several operations this was, so "saving the profile" beats the class name.
    /// </param>
    ErrorModalViewModel Record(Exception exception, string? context = null);

    /// <summary>
    /// The same, for a failure that never took the shape of an exception - a run that finished with
    /// some of its work undone, say.
    /// </summary>
    ErrorModalViewModel Record(string message, string? details = null, string? context = null);

    /// <inheritdoc cref="Record(Exception, string?)"/>
    /// <summary>Logs it and shows it. What almost every caller wants.</summary>
    Task ShowAsync(Exception exception, string? context = null);
}
