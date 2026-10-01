using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Profiles;

namespace ModsDude.Client.Wpf.Mods.Import;

public interface IModImportCoordinator
{
    /// <summary>
    /// Whether an import into this repo would be refused right now.
    /// </summary>
    /// <remarks>
    /// For a <c>CanExecute</c>, and a hint rather than the guard for the same reason
    /// <see cref="ProfileApplyService.IsBusy"/> is: it reads without claiming, so
    /// <see cref="RunAsync"/> is what actually decides.
    /// </remarks>
    bool IsBusy(Guid repoId);

    /// <summary>What is importing into this repo, named. Null when nothing is.</summary>
    string? DescribeBusy(Guid repoId);

    /// <summary>
    /// Registers <paramref name="versions"/> in the repo, reporting per row and on the strip.
    /// </summary>
    /// <param name="names">
    /// What each of these versions is called, keyed by identity - what the strip line and the failure
    /// modal both need, and the only thing about a caller's rows this has ever used.
    /// </param>
    /// <param name="progress">
    /// The caller's own sink, where it has rows to draw into. Null for a caller that owns no view -
    /// a save whose page has been navigated away from is still a run worth watching on the strip.
    /// </param>
    /// <param name="catalog">
    /// The caller's catalog, invalidated when the run ends however it ends. Passed rather than owned:
    /// the per-source scan cache and any ad-hoc folder somebody added off a USB stick are a browsing
    /// session's state, and a run that registered something still invalidates - otherwise the catalog
    /// goes on offering those versions for import all over again.
    /// </param>
    /// <param name="cancellationToken">
    /// The caller's, joined with the strip's Cancel. Either stops the run.
    /// </param>
    Task<ModImportOutcome> RunAsync(
        Repo repo,
        IReadOnlyList<CatalogModVersion> versions,
        IReadOnlyDictionary<ModVersionIdentity, string> names,
        ModCatalog catalog,
        IProgress<ModImportProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the copies the user chose against to the Recycle Bin, once whatever they were doing has
    /// succeeded.
    /// </summary>
    /// <remarks>
    /// Here rather than folded into <see cref="RunAsync"/> because <em>when</em> is the caller's to
    /// know - see the remarks on this class - and here rather than left to both callers because it is
    /// the same two lines either way.
    /// </remarks>
    /// <returns>How many files reached the Recycle Bin.</returns>
    int RecycleSuperseded(IReadOnlyList<ModSupersededFile> superseded);
}
