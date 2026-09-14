using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <param name="Result">What the run did, or null where it never started.</param>
/// <param name="Refusal">
/// Why it never started, in a sentence for the user. Null whenever <paramref name="Result"/> is not.
/// </param>
public sealed record ModImportOutcome(ModImportResult? Result, string? Refusal)
{
    public static ModImportOutcome Refused(string reason) => new(null, reason);
}


/// <summary>
/// The one way into an import: it claims the repo, puts the run on the shell strip with a Cancel
/// beside it, and answers the two questions an import cannot answer for itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>The counterpart of <see cref="ProfileApplyService"/>, and it exists for the same reason.</b> Two
/// surfaces import into a repo - the catalog page's Import button and the mod list editor's save,
/// which registers whatever the draft pins and is only on disk - and they had a copy each of the
/// strip entry, the two resolver dialogs and the request assembly. One copy of that is one too many
/// for code whose whole job is to agree with itself, and it left nowhere to put a claim that a third
/// surface could not forget.
/// </para>
/// <para>
/// <b>What it does not do is decide what the result means.</b> The two callers genuinely differ there:
/// the catalog page marks its rows and is finished, while the editor holds the problems dialog and the
/// superseded copies back until the save it was half of has actually committed - a file removed for a
/// revision that was never written is a file removed for nothing. So this hands back the raw result
/// and lets each of them do that themselves.
/// </para>
/// <para>
/// <b>Refused rather than queued</b>, exactly as an apply is: a second import into a repo mid-import
/// would upload the same bytes twice and have the two runs lose each other's placement races, and the
/// honest answer to somebody who pressed the button is that the first one is still going.
/// </para>
/// <para>
/// The modal host is taken lazily for the reason it is everywhere else: it is the shell, and the shell
/// builds the things that build this.
/// </para>
/// </remarks>
public sealed class ModImportCoordinator(
    ModImportService importService,
    ContentStoreMaintenance maintenance,
    Lazy<IModalService> modalService,
    IBackgroundTaskReporter backgroundTasks,
    IResourceLeases leases)
{
    /// <summary>
    /// Whether an import into this repo would be refused right now.
    /// </summary>
    /// <remarks>
    /// For a <c>CanExecute</c>, and a hint rather than the guard for the same reason
    /// <see cref="ProfileApplyService.IsBusy"/> is: it reads without claiming, so
    /// <see cref="RunAsync"/> is what actually decides.
    /// </remarks>
    public bool IsBusy(Guid repoId) => leases.IsHeld(ResourceKeys.Repo(repoId));

    /// <summary>What is importing into this repo, named. Null when nothing is.</summary>
    public string? DescribeBusy(Guid repoId) => leases.DescribeHolder(ResourceKeys.Repo(repoId));


    /// <summary>
    /// Registers <paramref name="versions"/> in the repo, reporting per row and on the strip.
    /// </summary>
    /// <param name="rows">
    /// The list rows these versions came from, keyed by identity - the only thing that knows what a
    /// mod is called, which is what a per-row bar and a failure dialog both need. Both callers hold
    /// the same kind of row, which is why this is one method rather than a generic one.
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
    public async Task<ModImportOutcome> RunAsync(
        Repo repo,
        IReadOnlyList<CatalogModVersion> versions,
        IReadOnlyDictionary<ModVersionIdentity, ModListItemViewModel> rows,
        ModCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
        {
            return new ModImportOutcome(ModImportResult.Empty, null);
        }

        var title = versions.Count == 1
            ? $"Importing 1 mod into '{repo.Name}'"
            : $"Importing {versions.Count} mods into '{repo.Name}'";

        using var lease = leases.TryAcquireExclusive(ResourceKeys.Repo(repo.Id), title);

        if (lease is null)
        {
            return ModImportOutcome.Refused(
                DescribeBusy(repo.Id) is string holder
                    ? $"{holder} is still running. Nothing was imported; try again when it finishes."
                    : $"Something is already importing into '{repo.Name}'. Nothing was imported.");
        }

        // Joined rather than replaced, so the page's own Cancel button and the strip's are the same
        // act on the same run - and so an import outlives the page it was started from with a way to
        // stop it, which is the half that was missing.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var task = backgroundTasks.Begin(title, cancel: cancellation.Cancel);

        var request = new ModImportRequest(repo.Id, versions, repo.Adapter.VersionComparer)
        {
            Progress = new ModImportRowProgress(rows, task),
            ResolveArbitration = ResolveArbitrationAsync,
            ResolveSourceConflicts = ResolveSourceConflictsAsync,

            // So the import leaves the store warm: what is uploaded from a folder the game does not
            // read is copied into the store these folders are served by, and the first sync after the
            // import finds it there instead of downloading it back.
            ModFolders = [.. repo.Games.SelectMany(x => x.Targets).Select(x => x.ModFolder)]
        };

        try
        {
            // The overload that invalidates the catalog when it is over, whatever it did: a cancelled
            // or partly failed import still registered something.
            var result = await importService.ImportAsync(catalog, request, cancellation.Token);

            return new ModImportOutcome(result, null);
        }
        finally
        {
            // An import seeds what it registered into the store serving these mod folders, so it is
            // one of only two things in the app that makes a store bigger - and unlike the other one
            // it has no sync behind it to sweep afterwards. Without this a run of imports walks a
            // store straight past its limit and leaves it there. In the finally because a cancelled
            // or partly failed import has still seeded whatever got that far.
            TidyStoresInBackground();
        }
    }

    /// <summary>
    /// Puts the stores back inside their limits after an import has grown one.
    /// </summary>
    /// <remarks>
    /// Not awaited and not reported: it is housekeeping nobody asked for, it skips any store still in
    /// use, and the import's own result is what the caller is waiting on. Making somebody watch a
    /// progress bar for it would be the opposite of the point.
    /// </remarks>
    private void TidyStoresInBackground()
    {
        _ = Task.Run(() => maintenance.SweepAllAsync(CancellationToken.None));
    }

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
    public int RecycleSuperseded(IReadOnlyList<ModSupersededFile> superseded)
    {
        return importService.RecycleSuperseded(superseded);
    }


    /// <summary>
    /// One dialog for the whole import, and only for the mods the comparer could not settle.
    /// Everything it settled is already registering by the time this is asked.
    /// </summary>
    /// <remarks>
    /// <b>Raised by the coordinator rather than by a page</b>, which is what stops it being a question
    /// asked on behalf of a view that has since been navigated away from and disposed. The strip
    /// entry behind it names the run either way, so the dialog is never the only thing on screen that
    /// knows what it is about.
    /// </remarks>
    private async Task<IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>?> ResolveArbitrationAsync(
        IReadOnlyList<ModVersionArbitrationItem> items,
        CancellationToken cancellationToken)
    {
        // The import runs off the UI thread, and everything from here down is view models a
        // dispatcher-bound modal is about to render.
        return await OnUiThreadAsync(async () =>
        {
            var modal = new ModVersionArbitrationModalViewModel(items);

            await modalService.Value.Show(modal);

            return modal.Result;
        });
    }

    /// <summary>
    /// Asked once per import, and only where two sources hold genuinely different files under one mod
    /// and version. Identical copies never reach here - there is nothing to choose between them.
    /// </summary>
    /// <inheritdoc cref="ResolveArbitrationAsync" path="/remarks"/>
    private async Task<IReadOnlyDictionary<ModVersionIdentity, string>?> ResolveSourceConflictsAsync(
        IReadOnlyList<ModSourceConflict> conflicts,
        CancellationToken cancellationToken)
    {
        return await OnUiThreadAsync(async () =>
        {
            var modal = new ModSourceConflictModalViewModel(conflicts);

            await modalService.Value.Show(modal);

            return modal.Result;
        });
    }

    /// <summary>
    /// Runs a question on the dispatcher and waits for its answer.
    /// </summary>
    /// <remarks>
    /// <c>InvokeAsync</c> of an async lambda hands back a task whose result is the inner task, so the
    /// unwrap is what makes awaiting this wait for the user rather than for the dialog to open.
    /// </remarks>
    private static Task<T> OnUiThreadAsync<T>(Func<Task<T>> work)
    {
        return Application.Current.Dispatcher.InvokeAsync(work).Task.Unwrap();
    }
}
