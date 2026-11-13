using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using PlannedPlacement = ModsDude.Client.Core.ModVersions.ModVersionPlacement;
using ServerPlacement = ModsDude.Client.Core.ModsDudeServer.Generated.ModVersionPlacement;

namespace ModsDude.Client.Core.Import;

/// <summary>
/// Turns selected catalog rows into registered mod versions: per mod, link then upload then
/// register, with a handful of mods doing that at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant is that a mod is never registered before its file is in storage</b>, and what
/// protects it is the <em>per-mod</em> ordering of those three steps. Running several mods at once
/// does not touch it: each mod's own sequence is unchanged, so the residue of a failure is at most
/// one orphaned blob per mod in flight rather than a dangling registration.
/// See docs/09-mod-catalog.md#per-mod-ordering-bounded-concurrency.
/// </para>
/// <para>
/// Versions of one mod register <b>sequentially</b>, in ascending order, each inserting before the
/// next version the server already knows about. Each insert depends on the previous one having
/// landed, so concurrency stays at the level of distinct mods - which is where it was anyway.
/// </para>
/// <para>
/// A version a remote source offers is <b>downloaded as the first step of its mod</b>, holding one
/// of <see cref="ModImportRequest.MaxConcurrentDownloads"/> slots until its file is imported and
/// deleted. A file that holds something other than the version offered is set aside, and once
/// everything else has finished the user is asked once what to do about all of them. Versions they
/// choose to use instead are imported in a second round.
/// </para>
/// <para>
/// A registered file is then <b>copied into the store that will serve it</b>, so the sync that
/// follows an import does not download bytes that never left the machine. That is housekeeping and
/// is treated as such: it happens after the registration it can no longer affect, it never fails a
/// version, and it is skipped where it would only duplicate - see <c>SeedStoreAsync</c>.
/// </para>
/// </remarks>
public sealed class ModImportService(
    IFilesClient filesClient,
    IModsClient modsClient,
    IModStore modStore,
    IModFileUploader uploader,
    IModImagePublisher imagePublisher,
    IRecycleBin recycleBin,
    IContentStoreProvider storeProvider,
    IRemoteModDownloader downloader,
    IRemoteDownloadNotes downloadNotes,
    ILogger<ModImportService> logger) : IModImportService
{
    public async Task<ModImportResult> ImportAsync(ModImportRequest request, CancellationToken cancellationToken)
    {
        using var run = new ImportRun(
            filesClient, modsClient, modStore, uploader, imagePublisher, storeProvider, downloader, downloadNotes, request, logger);

        return await run.RunAsync(cancellationToken);
    }

    public int RecycleSuperseded(IReadOnlyList<ModSupersededFile> superseded)
    {
        var recycled = 0;

        foreach (var file in superseded)
        {
            try
            {
                if (File.Exists(file.FilePath) is false)
                {
                    continue;
                }

                if (recycleBin.TryRecycle(file.FilePath))
                {
                    recycled++;
                    continue;
                }

                logger.LogWarning(
                    "Could not recycle {File}, superseded by another source's copy of {Mod} {Version}.",
                    file.FilePath, file.Identity.ModId.Value, file.Identity.VersionId.Value);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not recycle {File}.", file.FilePath);
            }
        }

        return recycled;
    }

    public async Task<ModImportResult> ImportAsync(ModCatalog catalog, ModImportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await ImportAsync(request, cancellationToken);
        }
        finally
        {
            catalog.Invalidate();
        }
    }


    private sealed class ImportRun(
        IFilesClient filesClient,
        IModsClient modsClient,
        IModStore modStore,
        IModFileUploader uploader,
        IModImagePublisher imagePublisher,
        IContentStoreProvider storeProvider,
        IRemoteModDownloader downloader,
        IRemoteDownloadNotes downloadNotes,
        ModImportRequest request,
        ILogger logger) : IDisposable
    {
        /// <summary>How many versions have their copies hashed at once. Disk bound, like the sync planner's.</summary>
        private const int _concurrentSourceChecks = 4;

        private readonly SemaphoreSlim _mods = new(Math.Max(1, request.MaxConcurrentMods));
        private readonly SemaphoreSlim _downloads = new(Math.Max(1, request.MaxConcurrentDownloads));

        /// <summary>Started with the first download, so a run that downloads nothing touches no folder.</summary>
        private readonly Lazy<IRemoteDownloadRun> _downloadRun = new(downloader.BeginRun);

        /// <summary>Downloads that held something else, waiting for the one question about all of them.</summary>
        private readonly List<MismatchedDownload> _mismatches = [];
        private readonly SemaphoreSlim _refetch = new(1, 1);
        private readonly Lock _results = new();
        private readonly List<ModImportItemResult> _items = [];

        /// <summary>
        /// The stores that will serve this repo's mod folders, one per distinct store, worked out
        /// once for the run. Lazy so an import that registers nothing never reads the settings.
        /// </summary>
        private readonly Lazy<IReadOnlyList<ContentStore>> _seedTargets = new(() =>
            [.. request.ModFolders
                .Select(storeProvider.GetStoreServing)
                .DistinctBy(x => FileSystemHelper.NormalizePathForComparison(x.RootPath))]);

        /// <summary>Which file each version is imported from, once that has been settled.</summary>
        private readonly Dictionary<ModVersionIdentity, ChosenCopy> _chosen = [];

        /// <summary>What the user chose against, for the versions that end up importing.</summary>
        private readonly List<ModSupersededFile> _superseded = [];

        private RegisteredVersions _registered = RegisteredVersions.Empty;


        public async Task<ModImportResult> RunAsync(CancellationToken cancellationToken)
        {
            var candidates = Triage();

            if (candidates.Count == 0)
            {
                return Result();
            }

            _registered = await FetchRegisteredAsync(cancellationToken);

            candidates = DropAlreadyRegistered(candidates, _registered);
            candidates = await ConfirmLikelyCopiesAsync(candidates, cancellationToken);

            if (candidates.Count == 0)
            {
                return Result();
            }

            candidates = await ResolveSourcesAsync(candidates, cancellationToken);

            await ImportAsync(candidates, _registered, cancellationToken);

            await ResolveMismatchesAsync(cancellationToken);

            return Result();
        }

        public void Dispose()
        {
            if (_downloadRun.IsValueCreated)
            {
                _downloadRun.Value.Dispose();
            }

            _mods.Dispose();
            _downloads.Dispose();
            _refetch.Dispose();
        }


        private async Task ImportAsync(
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            RegisteredVersions planned,
            CancellationToken cancellationToken)
        {
            if (candidates.Count == 0)
            {
                return;
            }

            var plan = ModVersionImportPlanner.Plan(
                candidates.Select(x => new ModVersionImportCandidate(x.Key, planned.For(x.Key), [.. x.Value.Keys])),
                request.Comparer);

            var running = new List<Task>();

            foreach (var ready in plan.Ready)
            {
                running.Add(Task.Run(() => RunModGuardedAsync(ready, null, candidates[ready.ModId], planned, cancellationToken), cancellationToken));
            }

            try
            {
                if (plan.Arbitration.Count > 0)
                {
                    // The ready mods are already running, so asking never holds them up.
                    await StartArbitratedAsync(plan.Arbitration, candidates, planned, running, cancellationToken);
                }
            }
            finally
            {
                // Completes only once every mod has, so a cancellation surfaces after the batch has
                // stopped rather than while parts of it are still writing results or files.
                await Task.WhenAll(running);
            }
        }


        /// <summary>
        /// Splits the selection into what can be imported and what is refused before any request is
        /// made, so nothing spends a round trip to be told what the catalog already knew.
        /// </summary>
        private Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> Triage()
        {
            var candidates = new Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>>();

            foreach (var version in request.Versions)
            {
                if (version.IsOnServer)
                {
                    Record(version, ModImportStatus.AlreadyRegistered);
                    Report(version, ModImportPhase.Completed);
                    continue;
                }

                // Sources that disagree are not decided here. Telling a duplicate from a genuine
                // disagreement means hashing archives, which is worth doing only for versions that
                // survive triage and the already-registered pass - see ResolveSourcesAsync.
                if (version.FoundIn.Count == 0 && (version.Download is null || request.ModAdapter is null))
                {
                    Refuse(version, ModImportStatus.NoLocalFile, "There is no local file to upload for this version.");
                    continue;
                }

                if (candidates.TryGetValue(version.ModId, out var versions) is false)
                {
                    candidates[version.ModId] = versions = [];
                }

                versions[version.VersionId] = version;
                Report(version, ModImportPhase.Queued);
            }

            return candidates;
        }

        /// <summary>
        /// Takes out the versions the repo turns out to already hold. The planner ignores those when
        /// placing, so without this they would silently vanish from the report - and a catalog
        /// snapshot taken before somebody else's import makes that ordinary rather than rare. It is
        /// the same success the link endpoint's <c>AlreadyRegistered</c> describes, reached a round
        /// trip earlier.
        /// </summary>
        private Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> DropAlreadyRegistered(
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            RegisteredVersions registered)
        {
            foreach (var (modId, versions) in candidates)
            {
                foreach (var versionId in registered.For(modId).Where(versions.ContainsKey).ToList())
                {
                    Record(versions[versionId], ModImportStatus.AlreadyRegistered);
                    Report(versions[versionId], ModImportPhase.Completed);

                    versions.Remove(versionId);
                }
            }

            return candidates
                .Where(x => x.Value.Count > 0)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// Asks once about every version whose mod looks like an accidental copy, such as <c>x (1)</c>,
        /// and leaves them out unless the user says otherwise.
        /// </summary>
        /// <remarks>
        /// A mod the repo already holds is not asked about again: somebody already chose to import it
        /// under that name.
        /// </remarks>
        private async Task<Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>>> ConfirmLikelyCopiesAsync(
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            CancellationToken cancellationToken)
        {
            var likelyCopies = candidates
                .Where(x => _registered.For(x.Key).Count == 0)
                .SelectMany(x => x.Value.Values)
                .Where(x => x.LikelyCopyOf is not null)
                .OrderBy(x => x.ModId.Value, StringComparer.Ordinal)
                .ThenBy(x => x.VersionId.Value, StringComparer.Ordinal)
                .ToList();

            if (likelyCopies.Count == 0)
            {
                return candidates;
            }

            var importAnyway = false;

            if (request.ResolveLikelyCopies is not null)
            {
                try
                {
                    importAnyway = await request.ResolveLikelyCopies(likelyCopies, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Losing the dialog leaves out the versions it was asking about, as a no would.
                    logger.LogWarning(exception, "Asking about mods that look like copies failed.");
                }
            }

            if (importAnyway)
            {
                return candidates;
            }

            foreach (var version in likelyCopies)
            {
                Record(version, ModImportStatus.LeftOut, $"Its name looks like a copy of '{version.LikelyCopyOf}'.");
                Report(version, ModImportPhase.Skipped);

                candidates[version.ModId].Remove(version.VersionId);
            }

            return candidates
                .Where(x => x.Value.Count > 0)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// Decides which file each version is imported from, and asks where the answer is not
        /// obvious.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Runs after the already-registered pass, deliberately.</b> This is the only phase that
        /// reads whole archives, and re-importing a folder the repo already holds should not pay for
        /// hashing files nothing is going to upload.
        /// </para>
        /// <para>
        /// <b>Identical copies are not a question.</b> Two sources holding the same bytes is the
        /// ordinary case - a mod in the mod folder and still in Downloads - and there is nothing to
        /// choose between them, so one is taken and nothing is said. Only genuinely different files
        /// reach the user, and only they can lead to anything being recycled.
        /// </para>
        /// </remarks>
        private async Task<Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>>> ResolveSourcesAsync(
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            CancellationToken cancellationToken)
        {
            var conflicts = new List<ModSourceConflict>();

            var local = candidates.Values
                .SelectMany(x => x.Values)
                .Where(x => IsDownload(x) is false)
                .OrderBy(x => x.ModId.Value, StringComparer.Ordinal)
                .ThenBy(x => x.VersionId.Value, StringComparer.Ordinal)
                .ToList();

            var resolved = new IReadOnlyList<ModFileCandidate>[local.Count];

            await Parallel.ForAsync(
                0,
                local.Count,
                new ParallelOptions { MaxDegreeOfParallelism = _concurrentSourceChecks, CancellationToken = cancellationToken },
                async (i, ct) =>
                {
                    var version = local[i];
                    var total = version.FoundIn.Count > 1 ? version.FoundIn.Sum(x => x.FileLength) : 0;

                    if (total > 0)
                    {
                        Report(version, ModImportPhase.Checking, 0, total);
                    }

                    resolved[i] = await ModOccurrenceResolver.ResolveAsync(
                        version.FoundIn,
                        total > 0 ? new InlineProgress<long>(x => Report(version, ModImportPhase.Checking, x, total)) : null,
                        ct);

                    if (total > 0)
                    {
                        Report(version, ModImportPhase.Queued);
                    }
                });

            for (var i = 0; i < local.Count; i++)
            {
                var version = local[i];
                var files = resolved[i];

                switch (files.Count)
                {
                    case 0:
                        // Every copy has gone, changed or will not open since the scan.
                        Refuse(version, ModImportStatus.NoLocalFile, "There is no local file to upload for this version.");
                        candidates[version.ModId].Remove(version.VersionId);
                        break;

                    case 1:
                        _chosen[version.Identity] = new ChosenCopy(files[0].Primary, files[0].ContentHash);
                        break;

                    default:
                        conflicts.Add(new ModSourceConflict(version, files));
                        break;
                }
            }

            if (conflicts.Count > 0)
            {
                await ResolveConflictsAsync(conflicts, candidates, cancellationToken);
            }

            return candidates
                .Where(x => x.Value.Count > 0)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// One dialog for every disagreeing version at once, and the same bargain the arbitration
        /// dialog strikes: an unanswered version is skipped and the rest of the batch carries on.
        /// </summary>
        private async Task ResolveConflictsAsync(
            IReadOnlyList<ModSourceConflict> conflicts,
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<ModVersionIdentity, string>? resolved = null;

            if (request.ResolveSourceConflicts is not null)
            {
                try
                {
                    resolved = await request.ResolveSourceConflicts(conflicts, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Losing the dialog costs the versions it was asking about and nothing else.
                    logger.LogWarning(exception, "Asking which source to import from failed.");
                }
            }

            foreach (var conflict in conflicts)
            {
                var version = conflict.Version;

                var chosen = resolved?.TryGetValue(version.Identity, out var key) is true
                    ? conflict.Candidates.FirstOrDefault(x => x.Key == key)
                    : null;

                if (chosen is null)
                {
                    Refuse(version, ModImportStatus.SourceConflict,
                        "Several sources hold different files for this mod and version, and nothing said which to import.");

                    candidates[version.ModId].Remove(version.VersionId);
                    continue;
                }

                _chosen[version.Identity] = new ChosenCopy(chosen.Primary, chosen.ContentHash);

                // Recorded now, recycled only if this version goes on to import - and by the caller,
                // not here. See ModSupersededFile. A copy in a game's mod folder is left to the next
                // apply of that folder, which replaces it through the engine.
                foreach (var rejected in conflict.Candidates.Where(x => x != chosen))
                {
                    foreach (var occurrence in rejected.Occurrences.Where(x => IsInGameFolder(x) is false))
                    {
                        _superseded.Add(new ModSupersededFile(
                            version.Identity, occurrence.FilePath, occurrence.Source.Name));
                    }
                }
            }
        }

        /// <summary>
        /// The file this version is being imported from. Everything that reads bytes, names them or
        /// measures them goes through here, so a resolved conflict cannot end up uploading one
        /// source's file under another source's name.
        /// </summary>
        private ModOccurrence Chosen(CatalogModVersion version)
            => _chosen.TryGetValue(version.Identity, out var chosen)
                ? chosen.Occurrence
                : version.FoundIn[0];

        /// <summary>What the chosen file hashed to when it was compared with other copies, or null where it was not.</summary>
        private string? ChosenHash(CatalogModVersion version)
            => _chosen.TryGetValue(version.Identity, out var chosen) ? chosen.ContentHash : null;

        private bool IsInGameFolder(ModOccurrence occurrence)
            => occurrence.Source.Kind is ModSourceKind.Game
            || request.ModFolders.Any(x => FileSystemHelper.ArePathsEqual(Path.GetDirectoryName(occurrence.FilePath), x));

        private async Task StartArbitratedAsync(
            IReadOnlyList<ModVersionArbitrationItem> arbitration,
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            RegisteredVersions planned,
            List<Task> running,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>? resolved = null;

            if (request.ResolveArbitration is not null)
            {
                try
                {
                    resolved = await request.ResolveArbitration(arbitration, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Losing the dialog costs the mods it was asking about and nothing else.
                    SkipArbitrated(arbitration, candidates, exception);
                    return;
                }
            }

            foreach (var item in arbitration)
            {
                if (resolved?.TryGetValue(item.ModId, out var order) is not true)
                {
                    SkipArbitrated([item], candidates, null);
                    continue;
                }

                ModVersionPlacementPlan plan;

                try
                {
                    plan = ModVersionPlacementPlanner.PlanFor(item.ModId, item.RegisteredInOrder, item.Incoming, order);
                }
                catch (ArgumentException exception)
                {
                    SkipArbitrated([item], candidates, exception);
                    continue;
                }

                running.Add(Task.Run(() => RunModGuardedAsync(plan, order, candidates[item.ModId], planned, cancellationToken), cancellationToken));
            }
        }

        private void SkipArbitrated(
            IReadOnlyList<ModVersionArbitrationItem> items,
            Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>> candidates,
            Exception? exception)
        {
            foreach (var item in items)
            {
                foreach (var version in candidates[item.ModId].Values)
                {
                    Refuse(version, ModImportStatus.NeedsArbitration,
                        "This mod's version order was not settled, so nothing was registered for it. It can be imported again later.",
                        exception);
                }
            }
        }


        /// <summary>
        /// One mod, where anything unexpected costs that mod's unfinished versions and never the
        /// result of the run: whatever else already registered stays reported.
        /// </summary>
        private async Task RunModGuardedAsync(
            ModVersionPlacementPlan plan,
            IReadOnlyList<ModVersionKey>? resolvedOrder,
            Dictionary<ModVersionKey, CatalogModVersion> versions,
            RegisteredVersions planned,
            CancellationToken cancellationToken)
        {
            try
            {
                await RunModAsync(plan, resolvedOrder, versions, planned, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Importing {Mod} stopped partway.", plan.ModId.Value);

                foreach (var version in versions.Values.Where(x => Outcome(x.Identity) is null))
                {
                    Refuse(version, ModImportStatus.Failed, "The import of this mod stopped partway.", exception);
                }
            }
        }

        /// <summary>
        /// One mod's versions, oldest first: downloaded where a remote source offers them, then
        /// registered. Holds a download slot from the first download until its files are imported and
        /// deleted, and an import slot for the whole mod rather than per version, because it is the
        /// mod that is one unit of work here.
        /// </summary>
        private async Task RunModAsync(
            ModVersionPlacementPlan plan,
            IReadOnlyList<ModVersionKey>? resolvedOrder,
            Dictionary<ModVersionKey, CatalogModVersion> versions,
            RegisteredVersions planned,
            CancellationToken cancellationToken)
        {
            var downloads = plan.Registrations
                .Select(x => versions[x.VersionId])
                .Where(IsDownload)
                .ToList();

            if (downloads.Count == 0)
            {
                await RegisterModAsync(plan, [.. plan.Registrations.Select(x => x.VersionId)], resolvedOrder, versions, planned, cancellationToken);

                return;
            }

            await _downloads.WaitAsync(cancellationToken);

            var downloaded = new List<string>();

            try
            {
                var remaining = plan.Registrations.Select(x => x.VersionId).ToList();

                foreach (var version in downloads)
                {
                    if (await DownloadAsync(version, versions, cancellationToken) is string folder)
                    {
                        downloaded.Add(folder);
                    }
                    else
                    {
                        remaining.Remove(version.VersionId);
                    }
                }

                if (remaining.Count == 0)
                {
                    return;
                }

                var current = plan;

                if (remaining.Count < plan.Registrations.Count)
                {
                    // A placement can name a neighbour that is no longer coming, so what is left is
                    // placed again against what the repo holds.
                    current = Replan(plan.ModId, planned.For(plan.ModId), remaining, resolvedOrder);

                    if (current.NeedsArbitration)
                    {
                        Refuse(versions, remaining, ModImportStatus.NeedsArbitration,
                            "This mod's version order was not settled, so nothing was registered for it. It can be imported again later.");

                        return;
                    }
                }

                await RegisterModAsync(current, remaining, resolvedOrder, versions, planned, cancellationToken);
            }
            finally
            {
                foreach (var folder in downloaded)
                {
                    _downloadRun.Value.Discard(folder);
                }

                _downloads.Release();
            }
        }

        private async Task RegisterModAsync(
            ModVersionPlacementPlan plan,
            List<ModVersionKey> remaining,
            IReadOnlyList<ModVersionKey>? resolvedOrder,
            Dictionary<ModVersionKey, CatalogModVersion> versions,
            RegisteredVersions planned,
            CancellationToken cancellationToken)
        {
            await _mods.WaitAsync(cancellationToken);

            try
            {
                var current = plan;
                var registered = planned;
                var attempts = 0;

                while (remaining.Count > 0)
                {
                    var conflicted = false;

                    foreach (var registration in current.Registrations)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (await ImportVersionAsync(versions[registration.VersionId], registration.Placement, cancellationToken)
                            is VersionOutcome.PlacementConflict)
                        {
                            conflicted = true;
                            break;
                        }

                        remaining.Remove(registration.VersionId);
                    }

                    if (conflicted is false)
                    {
                        break;
                    }

                    if (++attempts > request.MaxPlacementRetries)
                    {
                        Refuse(versions, remaining, ModImportStatus.Failed,
                            "The version order kept changing underneath this import. Try again.");
                        break;
                    }

                    // Optimistic concurrency: somebody else moved this mod's versions, so the
                    // placement this import computed is no longer true. Refetch, recompute against
                    // what is there now, and go again. The same path covers a version whose
                    // predecessor in this batch failed - its placement names a neighbour that never
                    // landed, and the recomputed one does not mention it.
                    try
                    {
                        registered = await RefetchRegisteredAsync(registered, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Could not refetch the repo's versions of {Mod}.", plan.ModId.Value);

                        foreach (var versionId in remaining)
                        {
                            Refuse(versions[versionId], ModImportStatus.Failed, "Could not read the repo's version order. Try again.", exception);
                        }

                        break;
                    }

                    // The refetch can also show that what is left has since been registered by
                    // somebody else, which is a success and not something to place again.
                    foreach (var versionId in registered.For(plan.ModId).Where(remaining.Contains).ToList())
                    {
                        Record(versions[versionId], ModImportStatus.AlreadyRegistered);
                        Report(versions[versionId], ModImportPhase.Completed);

                        remaining.Remove(versionId);
                    }

                    if (remaining.Count == 0)
                    {
                        break;
                    }

                    current = Replan(plan.ModId, registered.For(plan.ModId), remaining, resolvedOrder);

                    if (current.NeedsArbitration)
                    {
                        Refuse(versions, remaining, ModImportStatus.NeedsArbitration,
                            "The version order changed into one that needs deciding by hand. Import this mod again.");
                        break;
                    }
                }
            }
            finally
            {
                _mods.Release();
            }
        }

        /// <summary>
        /// Downloads one offered version and reads what it holds. Where that is the version offered,
        /// the version is replaced in <paramref name="versions"/> by the file's own record and the
        /// download's folder is returned; anything else is reported or set aside, and null returned.
        /// </summary>
        private async Task<string?> DownloadAsync(
            CatalogModVersion version,
            Dictionary<ModVersionKey, CatalogModVersion> versions,
            CancellationToken cancellationToken)
        {
            var offer = version.Download!;

            Report(version, ModImportPhase.Downloading);

            string folder;

            try
            {
                folder = await _downloadRun.Value.DownloadAsync(
                    offer,
                    new InlineProgress<RemoteDownloadProgress>(x => Report(version, ModImportPhase.Downloading, x.BytesReceived, x.TotalBytes ?? 0)),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Refuse(version, ModImportStatus.DownloadFailed,
                    exception is UserFriendlyException friendly ? friendly.UserMessage : "The download failed.",
                    exception);

                return null;
            }

            LocalMod? held;

            try
            {
                held = await ReadDownloadAsync(folder, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _downloadRun.Value.Discard(folder);
                throw;
            }
            catch (Exception exception)
            {
                // Not a verdict on the link: a file an antivirus still holds reads fine next time, so
                // no note is recorded and the version can be tried again.
                _downloadRun.Value.Discard(folder);
                Refuse(version, ModImportStatus.DownloadFailed, "The download could not be read.", exception);

                return null;
            }

            downloadNotes.Record(new RemoteDownloadNote(
                offer.Version.DownloadUrl,
                version.Identity,
                held is null ? null : new ModVersionIdentity(held.Id, held.Version)));

            if (held is not null && held.Id == version.ModId && held.Version == version.VersionId)
            {
                versions[version.VersionId] = FromDownload(held, offer, folder);

                return folder;
            }

            lock (_results)
            {
                _mismatches.Add(new MismatchedDownload(new ModDownloadMismatch(version, held), folder));
            }

            return null;
        }

        /// <summary>The mod a download's file holds, or null where it is not a mod. Throws where it could not be read.</summary>
        private async Task<LocalMod?> ReadDownloadAsync(string folder, CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(folder);

            return files.Length == 1
                ? await request.ModAdapter!.ReadModFileAsync(files[0], cancellationToken)
                : null;
        }

        /// <summary>
        /// One question for every download that held something else, asked once the rest has
        /// finished. What the user chooses to use instead is imported in a second round.
        /// </summary>
        private async Task ResolveMismatchesAsync(CancellationToken cancellationToken)
        {
            List<MismatchedDownload> mismatches;

            lock (_results)
            {
                mismatches = [.. _mismatches
                    .OrderBy(x => x.Mismatch.Offered.ModId.Value, StringComparer.Ordinal)
                    .ThenBy(x => x.Mismatch.Offered.VersionId.Value, StringComparer.Ordinal)];
            }

            if (mismatches.Count == 0)
            {
                return;
            }

            IReadOnlyDictionary<ModVersionIdentity, ModDownloadMismatchAnswer>? answers = null;

            if (request.ResolveDownloadMismatches is not null)
            {
                try
                {
                    answers = await request.ResolveDownloadMismatches([.. mismatches.Select(x => x.Mismatch)], cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Losing the dialog leaves the versions it was asking about unanswered.
                    logger.LogWarning(exception, "Asking what to do about mismatched downloads failed.");
                }
            }

            var replacements = new List<(MismatchedDownload Download, CatalogModVersion Held)>();

            foreach (var download in mismatches)
            {
                var (mismatch, folder) = download;
                var offered = mismatch.Offered;

                if (answers is null)
                {
                    Refuse(offered, ModImportStatus.DownloadMismatch, DescribeMismatch(mismatch));
                }
                else if (answers.GetValueOrDefault(offered.Identity, ModDownloadMismatchAnswer.LeaveOut) is ModDownloadMismatchAnswer.UseHeld
                    && mismatch.CanUseHeld)
                {
                    replacements.Add((download, FromDownload(mismatch.Held!, offered.Download!, folder)));
                    continue;
                }
                else
                {
                    Record(offered, ModImportStatus.LeftOut, DescribeMismatch(mismatch));
                    Report(offered, ModImportPhase.Skipped);
                }

                _downloadRun.Value.Discard(folder);
            }

            if (replacements.Count > 0)
            {
                await ImportReplacementsAsync(replacements, cancellationToken);
            }
        }

        private async Task ImportReplacementsAsync(
            List<(MismatchedDownload Download, CatalogModVersion Held)> replacements,
            CancellationToken cancellationToken)
        {
            try
            {
                var candidates = new Dictionary<ModKey, Dictionary<ModVersionKey, CatalogModVersion>>();

                foreach (var (_, held) in replacements)
                {
                    if (Outcome(held.Identity) is { IsSuccess: true })
                    {
                        continue;
                    }

                    if (candidates.TryGetValue(held.ModId, out var versions) is false)
                    {
                        candidates[held.ModId] = versions = [];
                    }

                    if (versions.TryAdd(held.VersionId, held))
                    {
                        Report(held, ModImportPhase.Queued);
                    }
                }

                if (candidates.Count > 0)
                {
                    try
                    {
                        _registered = await FetchRegisteredAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        foreach (var held in candidates.Values.SelectMany(x => x.Values))
                        {
                            Refuse(held, ModImportStatus.Failed, "Could not read the repo's mod list. Try again.", exception);
                        }

                        candidates = [];
                    }

                    if (candidates.Count > 0)
                    {
                        candidates = DropAlreadyRegistered(candidates, _registered);

                        await ImportAsync(candidates, _registered, cancellationToken);
                    }
                }

                foreach (var (download, held) in replacements)
                {
                    var offered = download.Mismatch.Offered;

                    if (Outcome(held.Identity) is { IsSuccess: true })
                    {
                        Record(offered, ModImportStatus.Replaced, DescribeMismatch(download.Mismatch), replacedBy: held.Identity);
                        Report(offered, ModImportPhase.Completed);
                    }
                    else
                    {
                        Refuse(offered, ModImportStatus.Failed, "The version the download held could not be imported.");
                    }
                }
            }
            finally
            {
                foreach (var (download, _) in replacements)
                {
                    _downloadRun.Value.Discard(download.Folder);
                }
            }
        }

        private static string DescribeMismatch(ModDownloadMismatch mismatch) => mismatch.Held switch
        {
            null => "The download is not a mod archive.",
            LocalMod held when held.Id != mismatch.Offered.ModId => "The download holds another mod.",
            LocalMod held => $"The download is version {held.Version.Value}, not {mismatch.Offered.VersionId.Value}."
        };

        /// <summary>A downloaded file as a version found on this machine, so it imports like any other.</summary>
        private static CatalogModVersion FromDownload(LocalMod mod, RemoteOffer offer, string folder)
            => CatalogModVersion.FromLocal(mod, [new ModOccurrence(offer.AsSource(folder), mod)]);

        /// <summary>Offered by a remote source, with no file on this machine yet.</summary>
        private static bool IsDownload(CatalogModVersion version) => version.Download is not null && version.FoundIn.Count == 0;

        private ModImportItemResult? Outcome(ModVersionIdentity identity)
        {
            lock (_results)
            {
                return _items.LastOrDefault(x => x.Identity == identity);
            }
        }

        private async Task<VersionOutcome> ImportVersionAsync(
            CatalogModVersion version,
            PlannedPlacement placement,
            CancellationToken cancellationToken)
        {
            try
            {
                try
                {
                    return await LinkAndUploadAsync(version, placement, cancellationToken);
                }
                catch (BlobAlreadyExistsException exception)
                {
                    // Another upload of the same version committed first. Asking for the link again
                    // reaches what that left: a registration, or a stored file to adopt or refuse.
                    logger.LogInformation(exception, "Mod {ModId} {VersionId} was stored by another upload first.", version.ModId.Value, version.VersionId.Value);

                    return await LinkAndUploadAsync(version, placement, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ModFileChangedException or UploadedContentChangedException)
            {
                Refuse(version, ModImportStatus.Failed, "The file changed after it was scanned. Rescan and import again.", exception);

                return VersionOutcome.Done;
            }
            catch (Exception exception)
            {
                // One mod's problem. The rest of the batch is unaffected, which is the whole point
                // of importing per mod rather than in phases.
                Refuse(version, ModImportStatus.Failed,
                    exception is UserFriendlyException friendly ? friendly.UserMessage : "The upload failed.",
                    exception);

                return VersionOutcome.Done;
            }
        }

        private async Task<VersionOutcome> LinkAndUploadAsync(
            CatalogModVersion version,
            PlannedPlacement placement,
            CancellationToken cancellationToken)
        {
            Report(version, ModImportPhase.Linking);

            CreateModUploadLinkResponse link;

            try
            {
                link = await CreateUploadLinkAsync(version, cancellationToken);
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.AlreadyRegistered)
            {
                // Nothing left to do. This is also what a teammate registering the same version
                // mid-import looks like from here, which is the same situation: the bytes this
                // import wanted are in the repo.
                Record(version, ModImportStatus.AlreadyRegistered);
                Report(version, ModImportPhase.Completed);

                return VersionOutcome.Done;
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.FileAlreadyPresent)
            {
                return await AdoptStoredFileAsync(version, placement, exception.Result.ContentHash, cancellationToken);
            }

            var total = Chosen(version).FileLength;

            Report(version, ModImportPhase.Uploading, 0, total);

            var hash = await uploader.UploadAsync(
                new ModFileUpload(link.Link, link.ContentHashMetadataKey, Chosen(version).OpenStream)
                {
                    BytesTransferred = new InlineProgress<long>(x => Report(version, ModImportPhase.Uploading, x, total)),
                    // A version picked between differing copies must upload the copy that was picked.
                    ExpectedHash = ChosenHash(version),
                    RenewLink = async ct => (await CreateUploadLinkAsync(version, ct)).Link
                },
                cancellationToken);

            return await RegisterAsync(version, placement, hash, cancellationToken);
        }

        private Task<CreateModUploadLinkResponse> CreateUploadLinkAsync(CatalogModVersion version, CancellationToken cancellationToken)
            => filesClient.CreateModUploadLinkV1Async(
                new CreateModUploadLinkRequest()
                {
                    RepoId = request.RepoId,
                    ModId = version.ModId.Value,
                    VersionId = version.VersionId.Value,
                    SizeBytes = Chosen(version).FileLength
                },
                cancellationToken);

        /// <summary>
        /// Finishes an import that already uploaded and then died: the blob is there, nothing is
        /// registered against it, and no upload link can ever be minted for that address again.
        /// </summary>
        /// <remarks>
        /// Registering describes the bytes that are really in the blob, so this can only proceed
        /// once those bytes are established. A recorded hash matching ours is this client's own
        /// orphan. One that differs is a different build wearing the same id and version, and one
        /// that is missing establishes nothing at all - both refuse, because registering over them
        /// writes a hash no download can ever satisfy and there is no repair path. See
        /// docs/07-mod-sync-design.md#hostile-or-wrong-hashes-have-to-be-unregisterable-not-just-undownloadable.
        /// </remarks>
        private async Task<VersionOutcome> AdoptStoredFileAsync(
            CatalogModVersion version,
            PlannedPlacement placement,
            string? storedHash,
            CancellationToken cancellationToken)
        {
            var ours = ChosenHash(version);

            if (ours is null)
            {
                using var content = Chosen(version).OpenStream();

                ours = await ModContentHasher.ComputeAsync(content, cancellationToken);
            }

            if (ModContentHasher.Matches(ours, storedHash) is false)
            {
                Refuse(version, ModImportStatus.ContentMismatch, storedHash is null
                    ? "A file is already stored for this mod and version and nothing records what it contains, so nothing can be registered against it."
                    : "A different file is already stored for this mod and version. Registering this one would describe bytes nobody could download.");

                return VersionOutcome.Done;
            }

            return await RegisterAsync(version, placement, ours, cancellationToken);
        }

        private async Task<VersionOutcome> RegisterAsync(
            CatalogModVersion version,
            PlannedPlacement placement,
            string contentHash,
            CancellationToken cancellationToken)
        {
            Report(version, ModImportPhase.Registering);

            // What the uploaded file itself says, not what the catalog row was merged from: two copies
            // of one version can disagree about their name, lock and attributes as well as their bytes.
            var chosen = Chosen(version);
            var mod = chosen.Mod;

            try
            {
                await modsClient.RegisterModV1Async(
                    request.RepoId,
                    new RegisterModRequest()
                    {
                        ModId = version.ModId.Value,
                        VersionId = version.VersionId.Value,
                        DisplayName = mod.Name,
                        Description = mod.Description,
                        // The repo learns what the file is called here and nowhere else, and every
                        // other member's mod folder is named from it. Taken off the occurrence the
                        // upload read, for the same reason the bytes are - a resolved conflict must
                        // not register one source's file under another source's name.
                        FileName = ModFileName.ForFile(version.ModId, chosen.FilePath).Value,
                        ContentHash = contentHash,
                        Locked = mod.Locked,
                        Placement = new ServerPlacement()
                        {
                            After = placement.After?.Value,
                            Before = placement.Before?.Value
                        },
                        Attributes = [.. mod.Attributes.Select(x => new ModAttributeDto() { Key = x.Key, Value = x.Value })]
                    },
                    cancellationToken);
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.VersionPlacementConflict)
            {
                return VersionOutcome.PlacementConflict;
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.HashMismatch)
            {
                // The stored file records other bytes than these: somebody else's upload of the same
                // version committed first.
                Refuse(version, ModImportStatus.ContentMismatch,
                    "A different file is already stored for this mod and version. Registering this one would describe bytes nobody could download.");

                return VersionOutcome.Done;
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.AlreadyExists)
            {
                // The same file registered already is answered as a success, so this is a different
                // file or name registered under the version between the link and here.
                Refuse(version, ModImportStatus.ContentMismatch,
                    "A different file is already registered for this mod and version.", exception);

                return VersionOutcome.Done;
            }

            // Imagery first: seeding moves a downloaded file into the store, after which it is gone
            // from where the imagery is read.
            var imageryError = await PublishImageryAsync(version, cancellationToken);

            await SeedStoreAsync(version, contentHash, cancellationToken);

            Record(version, ModImportStatus.Registered, imageryError);
            Report(version, ModImportPhase.Completed);

            return VersionOutcome.Done;
        }

        /// <summary>
        /// Copies the file that was just registered into the store that will serve it, so applying a
        /// profile containing it does not download bytes the machine already had.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Verified on the way in, like every other path into a store.</b> It streams through
        /// <see cref="ContentStore.IngestAsync"/> against the hash the registration recorded, which
        /// is the one check that makes a store shared between repos safe - and here it also costs
        /// nothing extra, since the bytes are being read to be copied anyway.
        /// </para>
        /// <para>
        /// <b>A file already sitting in one of these mod folders is left alone.</b> Sync will find it
        /// there and keep it, so copying it into the store would duplicate the archive for no gain -
        /// and on a 2,000-mod install that is tens of gigabytes. It reaches the store for free later,
        /// on the uninstall that displaces it. The case this exists for is the other one: a mod
        /// imported from Downloads or any folder the game does not read, which sync would otherwise
        /// have to fetch back from the server.
        /// </para>
        /// <para>
        /// <b>Never fails a version, and never fails the run.</b> The registration has already
        /// landed and is what the import was for; a store that could not be written is a cold store,
        /// not a failed import, so everything here is logged and swallowed - a cancellation included,
        /// since by this point the version's outcome is settled either way.
        /// </para>
        /// <para>
        /// <b>And for the same reason it takes no lease on the store</b>, where a sync's install does -
        /// see <see cref="Concurrency.IResourceLeases"/>. The worst a sweep or a clear racing this can
        /// do is delete what was just seeded, which costs exactly the download this was saving and is
        /// already the outcome every other failure here has. Waiting for housekeeping to finish before
        /// warming a cache would be the more expensive half of the bargain.
        /// </para>
        /// </remarks>
        private async Task SeedStoreAsync(CatalogModVersion version, string contentHash, CancellationToken cancellationToken)
        {
            if (_seedTargets.Value.Count == 0)
            {
                return;
            }

            var source = Chosen(version);
            var directory = Path.GetDirectoryName(source.FilePath);

            if (request.ModFolders.Any(x => FileSystemHelper.ArePathsEqual(directory, x)))
            {
                return;
            }

            // The store serving the disk the file is already on where there is one, so seeding is a
            // same-disk copy rather than a crossing. Otherwise the first, which is the only store
            // there is in every configuration but a multi-disk one.
            var target = _seedTargets.Value.FirstOrDefault(x =>
                string.Equals(x.VolumeRoot, FileSystemHelper.NormalizeVolumeRoot(source.FilePath), StringComparison.OrdinalIgnoreCase))
                ?? _seedTargets.Value[0];

            if (target.Contains(contentHash))
            {
                return;
            }

            try
            {
                Report(version, ModImportPhase.Storing);

                // A download is ModsDude's own and deleted after this anyway, so it is moved rather
                // than copied - a rename where the store is on the same disk.
                if (source.Source.Kind is ModSourceKind.Remote)
                {
                    await target.IngestFileAsync(source.FilePath, contentHash, cancellationToken);
                }
                else
                {
                    await using var content = source.OpenStream();

                    await target.IngestAsync(content, contentHash, null, cancellationToken);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not copy {Mod} {Version} into the content store at {Store} after importing it.",
                    version.ModId.Value, version.VersionId.Value, target.RootPath);
            }
        }

        /// <returns>Why imagery did not make it, or null. Never a reason to fail the version.</returns>
        private async Task<string?> PublishImageryAsync(CatalogModVersion version, CancellationToken cancellationToken)
        {
            // The occurrence the upload read, so the images published are the ones inside the file
            // that was actually registered rather than another copy's.
            var mod = Chosen(version).Mod;

            Report(version, ModImportPhase.PublishingImagery);

            try
            {
                await imagePublisher.PublishAsync(request.RepoId, version.ModId, version.VersionId, mod, cancellationToken);

                return null;
            }
            catch (Exception exception)
            {
                // Cancellation is swallowed here along with everything else, deliberately: the
                // version is registered by this point, and letting the token throw out of a
                // decoration step would lose that fact. The loop checks the token again anyway.
                logger.LogWarning(exception, "Imagery was not published for {Mod} {Version}.", version.ModId.Value, version.VersionId.Value);

                return "Imagery was not published.";
            }
        }


        private ModVersionPlacementPlan Replan(
            ModKey modId,
            IReadOnlyList<ModVersionKey> registeredInOrder,
            IReadOnlyList<ModVersionKey> remaining,
            IReadOnlyList<ModVersionKey>? resolvedOrder)
        {
            // An arbitrated order still holds if it covers exactly what is registered now plus what
            // is left to place. Where somebody else has moved the mod on underneath it there is no
            // honest way to reuse the answer, so the comparer gets another go - and if it still
            // cannot decide, the mod goes back to the dialog rather than being placed on a guess.
            if (resolvedOrder is not null && Covers(resolvedOrder, registeredInOrder, remaining))
            {
                return ModVersionPlacementPlanner.PlanFor(modId, registeredInOrder, remaining, resolvedOrder);
            }

            return ModVersionPlacementPlanner.Plan(modId, registeredInOrder, remaining, request.Comparer);
        }

        private static bool Covers(
            IReadOnlyList<ModVersionKey> resolvedOrder,
            IReadOnlyList<ModVersionKey> registeredInOrder,
            IReadOnlyList<ModVersionKey> remaining)
        {
            var expected = new HashSet<ModVersionKey>(registeredInOrder.Concat(remaining));

            return expected.Count == resolvedOrder.Count
                && expected.SetEquals(resolvedOrder)
                && resolvedOrder.Where(registeredInOrder.Contains).SequenceEqual(registeredInOrder);
        }

        private async Task<RegisteredVersions> RefetchRegisteredAsync(RegisteredVersions stale, CancellationToken cancellationToken)
        {
            await _refetch.WaitAsync(cancellationToken);

            try
            {
                // Several mods can lose the race at once, and one read answers all of them - so a
                // caller arriving behind somebody else's refetch takes that result.
                if (ReferenceEquals(_registered, stale) is false)
                {
                    return _registered;
                }

                return _registered = await FetchRegisteredAsync(cancellationToken);
            }
            finally
            {
                _refetch.Release();
            }
        }

        private async Task<RegisteredVersions> FetchRegisteredAsync(CancellationToken cancellationToken)
            => RegisteredVersions.From(await modStore.GetAsync(request.RepoId, cancellationToken));


        private void Report(CatalogModVersion version, ModImportPhase phase, long transferred = 0, long total = 0, string? error = null)
        {
            request.Progress?.Report(new ModImportProgress(version.Identity, phase)
            {
                BytesTransferred = transferred,
                TotalBytes = total,
                Error = error
            });
        }

        private void Record(
            CatalogModVersion version,
            ModImportStatus status,
            string? message = null,
            Exception? exception = null,
            ModVersionIdentity? replacedBy = null)
        {
            // The one funnel every unfinished item passes through, which is why the log line lives
            // here rather than at each of the four catch sites. What the user is shown names the
            // mods and the reason and deliberately no exception; this is where the rest of it goes.
            if (status is not (ModImportStatus.Registered or ModImportStatus.AlreadyRegistered or ModImportStatus.Replaced))
            {
                logger.LogWarning(
                    exception,
                    "{Mod} {Version} was not imported ({Status}): {Message}",
                    version.ModId.Value, version.VersionId.Value, status, message ?? "no reason given");
            }

            lock (_results)
            {
                _items.Add(new ModImportItemResult(version.Identity, status)
                {
                    Message = message,
                    Exception = exception,
                    ReplacedBy = replacedBy
                });
            }
        }

        private void Refuse(CatalogModVersion version, ModImportStatus status, string message, Exception? exception = null)
        {
            Record(version, status, message, exception);
            Report(version, status is ModImportStatus.Failed or ModImportStatus.DownloadFailed ? ModImportPhase.Failed : ModImportPhase.Skipped, error: message);
        }

        private void Refuse(
            Dictionary<ModVersionKey, CatalogModVersion> versions,
            IReadOnlyList<ModVersionKey> keys,
            ModImportStatus status,
            string message)
        {
            foreach (var key in keys)
            {
                Refuse(versions[key], status, message);
            }
        }

        private ModImportResult Result()
        {
            lock (_results)
            {
                // Superseded files follow their version's fate. A conflict that was resolved and
                // then failed to upload must not cost somebody the copy they chose against - the
                // repo has neither file, and the one still on disk is all that is left. Only this
                // import's own registration counts: one somebody else made may hold the rejected build.
                var imported = _items
                    .Where(x => x.Status is ModImportStatus.Registered)
                    .Select(x => x.Identity)
                    .ToHashSet();

                return new ModImportResult([.. _items])
                {
                    Superseded = [.. _superseded.Where(x => imported.Contains(x.Identity))]
                };
            }
        }


        /// <summary>A download set aside for the mismatch question, and the folder its file is in.</summary>
        private sealed record MismatchedDownload(ModDownloadMismatch Mismatch, string Folder);

        private enum VersionOutcome
        {
            /// <summary>Registered, already there, or reported. Either way this version is finished with.</summary>
            Done,

            /// <summary>The named neighbours are no longer adjacent, so the mod has to replan.</summary>
            PlacementConflict
        }

        /// <param name="ContentHash">What the copy hashed to when it was compared with others, or null where it was the only one.</param>
        private sealed record ChosenCopy(ModOccurrence Occurrence, string? ContentHash);

        /// <summary>The repo's versions per mod, in the order the repo stores them.</summary>
        private sealed class RegisteredVersions(Dictionary<ModKey, IReadOnlyList<ModVersionKey>> byMod)
        {
            public static RegisteredVersions Empty { get; } = new([]);


            public static RegisteredVersions From(IEnumerable<ModDto> mods)
            {
                // Normalized on the way in, exactly as the catalog does it: the server holds
                // whatever casing was registered, and an un-normalized id would silently miss the
                // mod it belongs to.
                var byMod = mods
                    .GroupBy(x => ModKey.From(x.ModId))
                    .ToDictionary(
                        x => x.Key,
                        x => (IReadOnlyList<ModVersionKey>)[.. x
                            .OrderBy(version => version.SequenceNumber)
                            .Select(version => ModVersionKey.From(version.VersionId))]);

                return new RegisteredVersions(byMod);
            }

            public IReadOnlyList<ModVersionKey> For(ModKey modId) => byMod.GetValueOrDefault(modId, []);
        }
    }
}
