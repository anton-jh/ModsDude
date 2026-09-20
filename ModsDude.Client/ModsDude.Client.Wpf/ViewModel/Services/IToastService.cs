namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>How loudly a toast says it. Failures are not among them - those are the error dialog's.</summary>
public enum ToastSeverity
{
    /// <summary>Something happened and went as expected.</summary>
    Info,

    /// <summary>Something was left as it was, or only partly done. Stays up longer and is drawn with a stripe.</summary>
    Warning
}


/// <summary>One thing a toast lets the user do, drawn as a link on the toast's own line.</summary>
/// <param name="Label">What the link says.</param>
/// <param name="Invoke">What pressing it does. The toast is dismissed as well.</param>
public sealed record ToastAction(string Label, Action Invoke);


/// <summary>A toast that was shown, for the one thing a caller may need to do with it afterwards.</summary>
public interface IToast
{
    /// <summary>
    /// Takes the toast down early. For an offer that stops being true - an undo, once something has
    /// been built on top of what it would restore. Harmless on one that is already gone.
    /// </summary>
    void Dismiss();
}


/// <summary>
/// Short-lived words at the bottom of the window: what a gesture just did, said once and then gone.
/// </summary>
/// <remarks>
/// <para>
/// <b>For results, not for faults.</b> A failure somebody may have to read twice, copy or report goes
/// through <see cref="IErrorReporter"/> and stays until it is closed. A toast disappears whether or not
/// it was read, so it is only ever the place for something the user could not have missed by looking
/// away - it happened, they pressed the button, and the sentence is confirmation.
/// </para>
/// <para>
/// <b>Not for state.</b> A sentence that has to be true for as long as the page is - why a button is
/// grey, what a list is currently filtered by - belongs on the page. A toast is an event.
/// </para>
/// <para>
/// <b>Safe from any thread</b>, so a service that finished work on a pool thread can report it
/// without marshalling.
/// </para>
/// </remarks>
public interface IToastService
{
    /// <param name="message">A sentence or two. It wraps, so it need not be short.</param>
    /// <param name="severity">See <see cref="ToastSeverity"/>.</param>
    /// <param name="actions">
    /// Links on the toast. A toast that offers something outlives one that only reports, so the offer
    /// can be read and decided on. A toast with actions is never merged with an identical one.
    /// </param>
    /// <returns>
    /// A handle to take it down early. Showing an identical toast while one is already up restarts the
    /// clock of the one on screen instead of stacking a copy, and the handle then belongs to a toast
    /// that was never drawn - which is why only a toast with actions, which is never merged, is worth
    /// holding on to.
    /// </returns>
    IToast Show(string message, ToastSeverity severity = ToastSeverity.Info, params ToastAction[] actions);
}
