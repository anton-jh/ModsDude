namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// Where a failure the app deliberately absorbs goes to be counted, so that absorbing it does not
/// also mean hiding it.
/// </summary>
/// <remarks>
/// Imagery, lazily loaded rows and cache writes are all things whose failure must not interrupt the
/// user - an error modal per row during an import of 2,000 mods is unusable, and that reasoning is
/// sound. What did not follow from it is that the user should never find out. Reports are counted,
/// aggregated and shown once, quietly, with the log behind them.
/// </remarks>
public interface IBackgroundProblemReporter
{
    /// <summary>Records one absorbed failure. Safe to call from any thread, and never throws.</summary>
    void Report(BackgroundProblem problem);
}


/// <summary>
/// Deliberately coarse. The notice says what stopped working in terms the user recognises; which
/// image, which row and which exception are the log's business.
/// </summary>
public enum BackgroundProblem
{
    /// <summary>Imagery derived from a mod could not be published to the repo.</summary>
    ImageUpload,

    /// <summary>An image could not be fetched or decoded for display.</summary>
    ImageDisplay,

    /// <summary>A row or panel could not finish loading what it draws.</summary>
    DeferredLoad
}
