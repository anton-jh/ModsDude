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
    IBackgroundTask Begin(string title, string? detail = null);
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
    void Report(string? detail, int completed, int total);

    /// <summary>Renames the work in flight, for something that only learns its own size part way in.</summary>
    void Retitle(string title);
}
