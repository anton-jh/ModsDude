using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Services;
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
/// Nothing here writes the imported file into a local content store. That would leave the store warm
/// after importing an existing install, but the store, its per-volume assignment and the settings
/// behind it are later phases; this is the seam it will attach to, once those exist.
/// See docs/PLAN.md Phase 1.
/// </para>
/// </remarks>
public sealed class ModImportService(
    IFilesClient filesClient,
    IModsClient modsClient,
    IModFileUploader uploader,
    IModImagePublisher imagePublisher,
    IRecycleBin recycleBin,
    ILogger<ModImportService> logger)
{
    public Task<ModImportResult> ImportAsync(ModImportRequest request, CancellationToken cancellationToken)
    {
        return new ImportRun(filesClient, modsClient, uploader, imagePublisher, request, logger).RunAsync(cancellationToken);
    }

    /// <summary>
    /// Sends the files the user chose against to the Recycle Bin, once whatever they were doing has
    /// actually succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called by the caller, not by the import.</b> The import knows a version registered; it does
    /// not know whether the profile save that registration was the first half of has committed. A
    /// file removed for a save that never happened is a file removed for nothing.
    /// </para>
    /// <para>
    /// <b>Best-effort, and never fatal.</b> A file the game is holding open stays where it is, which
    /// costs a duplicate on disk and nothing else - the repo already has the bytes that matter.
    /// Nothing here throws, and everything it could not do is in the log.
    /// </para>
    /// </remarks>
    /// <returns>How many files reached the Recycle Bin.</returns>
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

    /// <summary>
    /// The same import, invalidating the catalog it was selected from when it is over.
    /// </summary>
    /// <remarks>
    /// In a <c>finally</c> deliberately: a cancelled or partly failed import still registered
    /// something, and a catalog that kept claiming otherwise would offer those versions for import
    /// all over again.
    /// </remarks>
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
        IModFileUploader uploader,
        IModImagePublisher imagePublisher,
        ModImportRequest request,
        ILogger logger)
    {
        /// <summary>Matches the catalog's: the repo is expected to hold thousands of versions.</summary>
        private const int _pageSize = 500;

        private readonly SemaphoreSlim _mods = new(Math.Max(1, request.MaxConcurrentMods));
        private readonly SemaphoreSlim _refetch = new(1, 1);
        private readonly Lock _results = new();
        private readonly List<ModImportItemResult> _items = [];

        /// <summary>Which file each version is imported from, once that has been settled.</summary>
        private readonly Dictionary<ModVersionIdentity, ModOccurrence> _chosen = [];

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
            var planned = _registered;

            candidates = DropAlreadyRegistered(candidates, planned);

            if (candidates.Count == 0)
            {
                return Result();
            }

            candidates = await ResolveSourcesAsync(candidates, cancellationToken);

            if (candidates.Count == 0)
            {
                return Result();
            }

            var plan = ModVersionImportPlanner.Plan(
                candidates.Select(x => new ModVersionImportCandidate(x.Key, planned.For(x.Key), [.. x.Value.Keys])),
                request.Comparer);

            var running = new List<Task>();

            foreach (var ready in plan.Ready)
            {
                running.Add(Task.Run(() => RunModAsync(ready, null, candidates[ready.ModId], planned, cancellationToken), cancellationToken));
            }

            if (plan.Arbitration.Count > 0)
            {
                // The ready mods are already running, so asking never holds them up.
                await StartArbitratedAsync(plan.Arbitration, candidates, planned, running, cancellationToken);
            }

            // Completes only once every mod has, so a cancellation surfaces after the batch has
            // stopped rather than while parts of it are still writing results.
            await Task.WhenAll(running);

            return Result();
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
                if (version.FoundIn.Count == 0)
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

            foreach (var (_, versions) in candidates)
            {
                foreach (var version in versions.Values.ToList())
                {
                    var files = await ModOccurrenceResolver.ResolveAsync(version.FoundIn, cancellationToken);

                    switch (files.Count)
                    {
                        case 0:
                            // Every copy has gone or will not open since the scan.
                            Refuse(version, ModImportStatus.NoLocalFile, "There is no local file to upload for this version.");
                            versions.Remove(version.VersionId);
                            break;

                        case 1:
                            _chosen[version.Identity] = files[0].Primary;
                            break;

                        default:
                            conflicts.Add(new ModSourceConflict(version, files));
                            break;
                    }
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

                _chosen[version.Identity] = chosen.Primary;

                // Recorded now, recycled only if this version goes on to import - and by the caller,
                // not here. See ModSupersededFile.
                foreach (var rejected in conflict.Candidates.Where(x => x != chosen))
                {
                    foreach (var occurrence in rejected.Occurrences)
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
            => _chosen.TryGetValue(version.Identity, out var occurrence)
                ? occurrence
                : version.FoundIn[0];

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

                running.Add(Task.Run(() => RunModAsync(plan, order, candidates[item.ModId], planned, cancellationToken), cancellationToken));
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
        /// One mod's versions, oldest first. Holds a concurrency slot for the whole mod rather than
        /// per version, because it is the mod that is one unit of work here.
        /// </summary>
        private async Task RunModAsync(
            ModVersionPlacementPlan plan,
            IReadOnlyList<ModVersionKey>? resolvedOrder,
            Dictionary<ModVersionKey, CatalogModVersion> versions,
            RegisteredVersions planned,
            CancellationToken cancellationToken)
        {
            await _mods.WaitAsync(cancellationToken);

            try
            {
                var remaining = new List<ModVersionKey>(plan.Registrations.Select(x => x.VersionId));
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
                    registered = await RefetchRegisteredAsync(registered, cancellationToken);

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

        private async Task<VersionOutcome> ImportVersionAsync(
            CatalogModVersion version,
            PlannedPlacement placement,
            CancellationToken cancellationToken)
        {
            try
            {
                Report(version, ModImportPhase.Linking);

                CreateModUploadLinkResponse link;

                try
                {
                    link = await filesClient.CreateModUploadLinkV1Async(
                        new CreateModUploadLinkRequest()
                        {
                            RepoId = request.RepoId,
                            ModId = version.ModId.Value,
                            VersionId = version.VersionId.Value
                        },
                        cancellationToken);
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
                        BytesTransferred = new Forwarder<long>(x => Report(version, ModImportPhase.Uploading, x, total))
                    },
                    cancellationToken);

                return await RegisterAsync(version, placement, hash, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One mod's problem. The rest of the batch is unaffected, which is the whole point
                // of importing per mod rather than in phases.
                Refuse(version, ModImportStatus.Failed, exception.Message, exception);

                return VersionOutcome.Done;
            }
        }

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
            string ours;

            using (var content = Chosen(version).OpenStream())
            {
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

            try
            {
                await modsClient.RegisterModV1Async(
                    request.RepoId,
                    new RegisterModRequest()
                    {
                        ModId = version.ModId.Value,
                        VersionId = version.VersionId.Value,
                        DisplayName = version.Name,
                        Description = version.Description,
                        // The repo learns what the file is called here and nowhere else, and every
                        // other member's mod folder is named from it. Taken off the occurrence the
                        // upload read, for the same reason the bytes are - a resolved conflict must
                        // not register one source's file under another source's name.
                        FileName = ModFileName.ForFile(version.ModId, Chosen(version).FilePath).Value,
                        ContentHash = contentHash,
                        Locked = version.Locked,
                        Placement = new ServerPlacement()
                        {
                            After = placement.After?.Value,
                            Before = placement.Before?.Value
                        },
                        Attributes = []
                    },
                    cancellationToken);
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.VersionPlacementConflict)
            {
                return VersionOutcome.PlacementConflict;
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.AlreadyExists)
            {
                // Registered between the link and here. A success for the same reason
                // AlreadyRegistered is one.
                Record(version, ModImportStatus.AlreadyRegistered);
                Report(version, ModImportPhase.Completed);

                return VersionOutcome.Done;
            }

            var imageryError = await PublishImageryAsync(version, cancellationToken);

            Record(version, ModImportStatus.Registered, imageryError);
            Report(version, ModImportPhase.Completed);

            return VersionOutcome.Done;
        }

        /// <returns>Why imagery did not make it, or null. Never a reason to fail the version.</returns>
        private async Task<string?> PublishImageryAsync(CatalogModVersion version, CancellationToken cancellationToken)
        {
            if (ToLocalMod(version) is not LocalMod mod)
            {
                return null;
            }

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

                return $"Imagery was not published: {exception.Message}";
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
                // Several mods can lose the race at once, and one walk of the mod list answers all
                // of them - so a caller arriving behind somebody else's refetch takes that result
                // rather than paging the whole repo again.
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

        /// <remarks>
        /// The whole list, because there is no endpoint that answers "the versions of this one mod"
        /// - and the first fetch is needed in full anyway to plan the batch.
        /// </remarks>
        private async Task<RegisteredVersions> FetchRegisteredAsync(CancellationToken cancellationToken)
        {
            var mods = new List<ModDto>();
            string? cursor = null;

            do
            {
                var page = await modsClient.GetModsV1Async(request.RepoId, null, cursor, _pageSize, cancellationToken);

                mods.AddRange(page.Mods);
                cursor = page.NextCursor;
            }
            while (string.IsNullOrEmpty(cursor) is false);

            return RegisteredVersions.From(mods);
        }


        /// <summary>
        /// The archive the upload took its bytes from, in the shape the image publisher wants - the
        /// same occurrence, so the images published are the ones inside the file that was actually
        /// registered rather than a rejected copy's.
        /// </summary>
        private LocalMod? ToLocalMod(CatalogModVersion version)
        {
            if (version.FoundIn.Count == 0)
            {
                return null;
            }

            var occurrence = Chosen(version);

            return new LocalMod(version.ModId, version.VersionId, version.Name, version.Description, occurrence.OpenStream)
            {
                FilePath = occurrence.FilePath,
                FileLength = occurrence.FileLength,
                Icon = version.Icon,
                Images = version.Images,
                Author = version.Author
            };
        }


        private void Report(CatalogModVersion version, ModImportPhase phase, long transferred = 0, long total = 0, string? error = null)
        {
            request.Progress?.Report(new ModImportProgress(version.Identity, phase)
            {
                BytesTransferred = transferred,
                TotalBytes = total,
                Error = error
            });
        }

        private void Record(CatalogModVersion version, ModImportStatus status, string? message = null, Exception? exception = null)
        {
            // The one funnel every unfinished item passes through, which is why the log line lives
            // here rather than at each of the four catch sites. What the user is shown names the
            // mods and the reason and deliberately no exception; this is where the rest of it goes.
            if (status is not (ModImportStatus.Registered or ModImportStatus.AlreadyRegistered))
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
                    Exception = exception
                });
            }
        }

        private void Refuse(CatalogModVersion version, ModImportStatus status, string message, Exception? exception = null)
        {
            Record(version, status, message, exception);
            Report(version, status is ModImportStatus.Failed ? ModImportPhase.Failed : ModImportPhase.Skipped, error: message);
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
                // repo has neither file, and the one still on disk is all that is left.
                var imported = _items
                    .Where(x => x.IsSuccess)
                    .Select(x => x.Identity)
                    .ToHashSet();

                return new ModImportResult([.. _items])
                {
                    Superseded = [.. _superseded.Where(x => imported.Contains(x.Identity))]
                };
            }
        }


        private enum VersionOutcome
        {
            /// <summary>Registered, already there, or reported. Either way this version is finished with.</summary>
            Done,

            /// <summary>The named neighbours are no longer adjacent, so the mod has to replan.</summary>
            PlacementConflict
        }

        /// <summary>
        /// Reports on the calling thread. <see cref="Progress{T}"/> would post to whatever context
        /// happened to be current, which for byte counts arriving thousands of times per file is
        /// both slower and out of order.
        /// </summary>
        private sealed class Forwarder<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }

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
