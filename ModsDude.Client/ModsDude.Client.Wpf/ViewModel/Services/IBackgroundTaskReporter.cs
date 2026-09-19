using ModsDude.Client.Core.Transfers;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// What anything long-running tells the shell so the user can see it is happening.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IBackgroundProblemReporter"/>, and registered the same way: one
/// object, under this interface for the things that report and under its own type for the shell that
/// draws them. The pages keep their own detailed progress - a two thousand row import needs a bar per
/// row, and this could never be that - and what this adds is the half those cannot: an import or a
/// check-in stays visible after the user has navigated to another page, which they will, because the
/// whole point of the work being asynchronous is that they can.
/// </para>
/// <para>
/// <b>Deliberately not a modal.</b> Nothing here blocks: the operations are safe to leave running and
/// the user has other things to look at while they do. A modal would also have to be dismissed by
/// whatever finished last, which is exactly the bookkeeping this is meant to remove from the views.
/// </para>
/// <para>
/// <b>And not the guard either.</b> What stops two applies landing in one mod folder is a lease on the
/// folder - see <see cref="Core.Concurrency.IResourceLeases"/> - not anything on screen. This strip
/// reports; it does not protect. The two are easy to confuse, because a modal would appear to do both
/// and in fact does neither: an apply reached from the drift notice goes through no page at all.
/// </para>
/// </remarks>
public interface IBackgroundTaskReporter
{
    /// <summary>
    /// Announces a piece of work and hands back the handle that reports on it. <b>Dispose ends it</b>
    /// - so a <c>using</c> covers the failure and cancellation paths without a <c>finally</c> in every
    /// caller, which is the way a progress indicator gets left on screen forever.
    /// </summary>
    /// <param name="title">
    /// What is happening, named for the person watching: "Importing 42 mods into Vanilla", not
    /// "ImportRun".
    /// </param>
    /// <param name="cancel">
    /// How to stop it, where it can be stopped. Given, the strip draws a Cancel button.
    /// </param>
    /// <remarks>
    /// <b>The cancel hook is the only reason the strip is more than a label.</b> The button that
    /// started a long job lives on a page, and a page is rebuilt from scratch on every navigation - so
    /// the moment somebody uses the very freedom this strip exists to give them, the Cancel they had
    /// is gone and the work is unstoppable. The strip outlives the page, which makes it the only
    /// honest place to put it.
    /// </remarks>
    IBackgroundTask Begin(string title, string? detail = null, Action? cancel = null);
}


/// <summary>One running piece of work, for as long as it is running.</summary>
public interface IBackgroundTask : IDisposable
{
    /// <summary>Where it has got to, with no proportion to report. Leaves the bar indeterminate.</summary>
    void Report(string? detail);

    /// <summary>
    /// Where it has got to, and how far through. A <paramref name="total"/> of zero or less is "not
    /// countable", which is the same as reporting a detail alone rather than a bar stuck at nothing.
    /// </summary>
    /// <param name="amount">
    /// The proportion said in the caller's own words - "41.2 MB / 68 MB" - where a count of things
    /// would not mean anything. Replaces the "12 of 40" the strip would otherwise write; null keeps
    /// it. Pre-formatted rather than a unit, because only the caller knows what it is counting.
    /// </param>
    void Report(string? detail, long completed, long total, string? amount = null);

    /// <summary>Renames the work in flight, for something that only learns its own size part way in.</summary>
    void Retitle(string title);

    /// <summary>
    /// Says this task moves bytes to or from storage, so the strip can name a speed limit that is
    /// holding it back - and keep naming it correctly if the limit changes while it runs.
    /// </summary>
    void DeclareTransfers(TransferDirection directions);

    /// <summary>
    /// Announces one part of this task, which gets a row and a bar of its own once it proves slow.
    /// <b>Dispose ends it</b>, as with the task itself; ending the task ends whatever it still holds.
    /// </summary>
    /// <param name="longRunning">
    /// True where the caller already knows this one is slow - a 600 MB upload, whose size is on the
    /// first report. Otherwise the strip decides by the clock, and the two are the same mechanism:
    /// declaring it only skips the wait.
    /// </param>
    /// <remarks>
    /// <b>The fast ones are counted, not drawn.</b> Five parallel uploads writing their names into one
    /// detail line is a line nobody can read, and five rows churning under it is the same problem with
    /// the strip's own height added to it. So a subtask earns a row by outliving the threshold, and
    /// everything below it is a number on the task's line.
    /// </remarks>
    IBackgroundSubtask BeginSubtask(string name, bool longRunning = false);
}


/// <summary>One part of a running task. Only the slow ones are drawn.</summary>
public interface IBackgroundSubtask : IDisposable
{
    /// <summary>
    /// How far through this part is. A <paramref name="total"/> of zero or less leaves its bar
    /// indeterminate, which is the ordinary case for work that is slow without being countable.
    /// </summary>
    /// <param name="amount">As on <see cref="IBackgroundTask.Report(string?, long, long, string?)"/>.</param>
    void Report(long completed, long total, string? amount = null);

    /// <summary>Renames this part, for one that changes what it is doing without ending.</summary>
    void Rename(string name);
}
