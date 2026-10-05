using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Sync;

/// <summary>Carries out a <see cref="ModSyncPlan"/>.</summary>
internal sealed class ModSyncExecutor(
    IFilesClient filesClient,
    IModFileDownloader downloader,
    IContentStoreProvider storeProvider,
    ISyncManifestStore manifestStore,
    IRecycleBin recycleBin,
    IModFolders modFolders,
    ISavegamePlayAttribution playAttribution,
    IResourceLeases leases,
    IGameFileEditor fileEditor,
    IGameRunningGuard runningGuard,
    TimeProvider time,
    ILogger logger)
{
    /// <summary>
    /// Mods fetched at once. Their range requests share one connection budget in the downloader,
    /// so this buys overlap for small files rather than more connections for large ones.
    /// </summary>
    private const int _concurrentFetches = 4;


    public event Action<string>? ModFolderChanged;


    /// <remarks>
    /// <para>
    /// <b>Runs under a shared lease on every store it reads.</b> The additive half of a store is
    /// already safe beside any number of other syncs - see
    /// <see cref="IResourceLeases.AcquireSharedAsync"/> - so this claims the readers' side and
    /// conflicts with nothing except the three things that delete: the settings page's sweep, its two
    /// clears, and a verify pass. Waiting for one of those is bounded, and the alternative is a
    /// fetched blob deleted between being placed and being linked.
    /// </para>
    /// <para>
    /// <b>The folder itself is not claimed here.</b> That lease belongs to the gesture rather than to
    /// one folder's execution - it has to cover the plan, the confirmation and the work as one thing -
    /// so it is taken by <c>ProfileApplyService</c> and every route into this method comes through it.
    /// </para>
    /// </remarks>
    public async Task<ModSyncResult> ExecuteAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        runningGuard.EnsureNotRunning(plan.Game, plan.GameName);

        var failures = new List<ModSyncFailure>();
        var quarantined = new List<QuarantinedFile>();
        var completed = false;
        var folderTouched = false;

        // Scoped tightly, because the sweep below wants the other side of this very lease.
        using (await leases.AcquireSharedAsync(
            plan.AllStores.Select(ResourceKeys.Store),
            $"Applying to {plan.ModFolder}",
            cancellationToken))
        {
            await FetchAsync(plan, progress, failures, cancellationToken);

            if (failures.Count > 0)
            {
                // Nothing in the mod folder has been touched, so stopping here leaves the game
                // exactly as it was rather than half-applied.
                return new ModSyncResult(false, failures);
            }

            // From here the folder is being changed, so whatever happens next, what anybody has cached
            // about it is out of date. Announced once the lease is let go of, below.
            folderTouched = plan.HasWork;

            try
            {
                var stillInPlace = await RemoveAsync(plan, progress, failures, quarantined, cancellationToken);
                await InstallAsync(plan, stillInPlace, progress, failures, cancellationToken);
                await ApplyManagedFilesAsync(plan, failures, cancellationToken);
            }
            catch
            {
                AnnounceChange(plan);

                throw;
            }

            completed = failures.Count == 0;

            progress?.Report(new ModSyncProgress(ModSyncPhase.Finishing, 0, 1));

            if (completed)
            {
                await WriteManifestAsync(plan);
            }
        }

        if (folderTouched)
        {
            AnnounceChange(plan);
        }

        var eviction = Evict(plan, cancellationToken);

        return new ModSyncResult(completed, failures)
        {
            Quarantined = quarantined,
            Eviction = eviction,
            ManifestWritten = completed
        };
    }


    private void AnnounceChange(ModSyncPlan plan)
    {
        try
        {
            ModFolderChanged?.Invoke(plan.ModFolder);
        }
        catch (Exception exception)
        {
            // A listener that throws is the listener's bug, and it must not turn an apply that worked into one
            // that is reported as failing.
            logger.LogError(exception, "A handler of ModFolderChanged failed for {Folder}.", plan.ModFolder);
        }
    }

    /// <summary>
    /// Fills the serving store: another disk's store first, the network second. A disk-to-disk copy
    /// beats a download every time and leaves the blob local for the next install to this disk.
    /// </summary>
    /// <remarks>
    /// <b>Several at once</b>, because a download's cost is mostly per file - a link to mint, a first
    /// byte to wait for, one connection's ceiling - and most mods are small. Copies from another
    /// store still go one at a time: several at once on one spinning disk is slower than one.
    /// See docs/07-mod-sync-design.md#downloading.
    /// </remarks>
    private async Task FetchAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        List<ModSyncFailure> failures,
        CancellationToken cancellationToken)
    {
        // Asked of the store again rather than read off the plan: another apply may have fetched some
        // of these while this one's plan was being confirmed.
        var wanted = plan.Items
            .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace)
            .Where(x => x.DesiredHash is not null && plan.ServingStore.Contains(x.DesiredHash) is false)
            .GroupBy(x => x.DesiredHash!, StringComparer.OrdinalIgnoreCase)
            .Select(x => (Hash: x.Key, Item: x.First()))
            .ToList();

        var run = new FetchRun(wanted.Count, progress);
        using var copying = new SemaphoreSlim(1);

        await Parallel.ForEachAsync(
            wanted,
            new ParallelOptions { MaxDegreeOfParallelism = _concurrentFetches, CancellationToken = cancellationToken },
            async (fetch, ct) =>
            {
                run.Report(fetch.Item, 0, 0);

                try
                {
                    await FetchOneAsync(plan, fetch.Item, fetch.Hash, run, copying, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    RecordFailure(failures, fetch.Item, exception);
                }

                run.Finish(fetch.Item);
            });

        progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, wanted.Count, wanted.Count));
    }

    private async Task FetchOneAsync(
        ModSyncPlan plan,
        ModSyncItem item,
        string hash,
        FetchRun run,
        SemaphoreSlim copying,
        CancellationToken cancellationToken)
    {
        var elsewhere = plan.AllStores.FirstOrDefault(x =>
            FileSystemHelper.ArePathsEqual(x.RootPath, plan.ServingStore.RootPath) is false &&
            x.Contains(hash));

        // Known up front for a cross-store copy, and only once the response headers arrive for a
        // download - so the row can show a proportion in both cases rather than only one.
        var totalBytes = elsewhere?.GetSize(hash) ?? 0;

        // Two sources on a download - bytes arriving and bytes stored - and the bar follows whichever
        // is further on. Arriving leads while a ranged download is running; stored has the last word
        // on a single stream, where the downloader leaves it to the store.
        var gate = new Lock();
        long shown = 0;

        var report = new Forwarder<long>(x =>
        {
            lock (gate)
            {
                if (x > shown)
                {
                    shown = x;
                    run.Report(item, x, totalBytes);
                }
            }
        });

        if (elsewhere is not null)
        {
            await copying.WaitAsync(cancellationToken);

            try
            {
                await plan.ServingStore.CopyFromAsync(elsewhere, hash, report, cancellationToken);
            }
            finally
            {
                copying.Release();
            }

            return;
        }

        var link = await filesClient.CreateModDownloadLinkV1Async(
            new CreateModDownloadLinkRequest
            {
                RepoId = plan.RepoId,
                ModId = item.ModId.Value,
                VersionId = item.DesiredVersion?.Value ?? throw new InvalidOperationException("An install has no version to download.")
            },
            cancellationToken);

        using var download = await downloader.OpenAsync(link.Link, report, cancellationToken);

        totalBytes = download.Length ?? 0;

        // Verified against what the repo declared before it is stored, never after. This is the
        // check that makes a store shared between repos safe; see ContentStore.IngestAsync.
        await plan.ServingStore.IngestAsync(download.Content, hash, report, cancellationToken);
    }

    /// <summary>
    /// The destructive phase, under the uninstall rules: a file whose bytes the repo can reproduce is
    /// deleted once some store holds them, and anything else goes to the Recycle Bin.
    /// </summary>
    /// <returns>The mods whose old file could not be moved out of the way, which are then not installed.</returns>
    private async Task<IReadOnlySet<ModKey>> RemoveAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        List<ModSyncFailure> failures,
        List<QuarantinedFile> quarantined,
        CancellationToken cancellationToken)
    {
        var removals = plan.Items
            .Where(x => x.InstalledPath is not null)
            .Where(x => x.Action is ModSyncAction.Replace or ModSyncAction.UninstallRecoverable or ModSyncAction.Quarantine)
            .ToList();

        var runStartedAt = time.GetUtcNow();
        var stillInPlace = new HashSet<ModKey>();
        var completed = 0;

        foreach (var item in removals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ModSyncProgress(ModSyncPhase.Removing, completed, removals.Count)
            {
                ModId = item.ModId.Value,
                Detail = item.DisplayName
            });

            try
            {
                if (item.InstalledIsRecoverable && item.InstalledHash is string hash && IsAsPlanned(item))
                {
                    await KeepIfNothingElseHasItAsync(plan, item.InstalledPath!, hash, cancellationToken);
                }
                else
                {
                    quarantined.Add(Quarantine(plan, item, runStartedAt));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordFailure(failures, item, exception);
                stillInPlace.Add(item.ModId);
            }

            completed++;
        }

        progress?.Report(new ModSyncProgress(ModSyncPhase.Removing, completed, removals.Count));

        return stillInPlace;
    }

    /// <summary>
    /// Whether the file is still the one the plan hashed. Anything written since is treated as
    /// unrecognised and moved aside rather than deleted.
    /// </summary>
    private static bool IsAsPlanned(ModSyncItem item)
    {
        var info = new FileInfo(item.InstalledPath!);

        return info.Exists
            && info.Length == item.InstalledSize
            && info.LastWriteTimeUtc == item.InstalledModifiedUtc;
    }

    private void RecordFailure(List<ModSyncFailure> failures, ModSyncItem item, Exception exception)
    {
        logger.LogError(exception, "{Action} failed for {Mod} during sync.", item.Action, item.ModId.Value);

        lock (failures)
        {
            failures.Add(new ModSyncFailure(item.DisplayName, exception.Message) { Exception = exception });
        }
    }

    /// <summary>
    /// Moves the file into the serving store only where no store on the machine holds those bytes
    /// already; otherwise it is simply deleted.
    /// </summary>
    /// <remarks>
    /// Checking the stores beats hashing the file: one of them usually has it, and rehashing 2,000
    /// archives to discover that is minutes of pointless I/O. On a hardlink-served disk the file
    /// <em>is</em> the store entry, so the store has it and the delete is genuinely free. Declining
    /// to keep a copy leans on another store's, which is subject to that store's eviction - "no
    /// download needed right now" rather than "present forever" - and that is the right trade
    /// against duplicating a mod onto a disk the user chose to keep free.
    /// </remarks>
    private static async Task KeepIfNothingElseHasItAsync(ModSyncPlan plan, string path, string hash, CancellationToken cancellationToken)
    {
        if (plan.AllStores.Any(x => x.Contains(hash)))
        {
            File.Delete(path);

            return;
        }

        await plan.ServingStore.IngestFileAsync(path, hash, removeSource: true, cancellationToken);
    }

    private QuarantinedFile Quarantine(ModSyncPlan plan, ModSyncItem item, DateTimeOffset runStartedAt)
    {
        var path = item.InstalledPath!;

        // The user's own choice comes first, where they made one. A folder that then cannot be written
        // to - a drive that went away between the dialog and the work - falls through to the bin rather
        // than leaving the file where it blocks the install: they asked for the file to be kept, and
        // the bin keeps it.
        if (plan.QuarantineFolder is string chosen && TryMoveInto(chosen, path, out var moved))
        {
            return new QuarantinedFile(item.ModId, path, QuarantineDestination.ChosenFolder) { Path = moved };
        }

        if (recycleBin.TryRecycle(path))
        {
            return new QuarantinedFile(item.ModId, path, QuarantineDestination.RecycleBin);
        }

        // A drive with the Recycle Bin turned off, or a network path. The file moves into the store's
        // quarantine folder, and where that fails too it stays in the mod folder and the item fails.
        try
        {
            return new QuarantinedFile(item.ModId, path, QuarantineDestination.QuarantineFolder)
            {
                Path = MoveInto(plan.ServingStore.GetQuarantineDirectory(runStartedAt), path)
            };
        }
        catch (Exception exception)
        {
            throw new IOException($"'{Path.GetFileName(path)}' could not be moved out of the mod folder, so it was left where it is.", exception);
        }
    }

    private bool TryMoveInto(string folder, string path, out string destination)
    {
        try
        {
            destination = MoveInto(folder, path);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not move {File} into the chosen folder {Folder}.", path, folder);

            destination = string.Empty;

            return false;
        }
    }

    /// <summary>Moves a file into a folder under a name nothing there already has.</summary>
    private static string MoveInto(string folder, string path)
    {
        Directory.CreateDirectory(folder);

        var destination = FileSystemHelper.GetUnusedPath(folder, Path.GetFileName(path));

        File.Move(path, destination);

        return destination;
    }

    /// <param name="stillInPlace">
    /// Mods whose old file, or a file blocking the new one, could not be moved aside. Installing them
    /// would put a second file beside the first, or fail on it.
    /// </param>
    private async Task InstallAsync(
        ModSyncPlan plan,
        IReadOnlySet<ModKey> stillInPlace,
        IProgress<ModSyncProgress>? progress,
        List<ModSyncFailure> failures,
        CancellationToken cancellationToken)
    {
        // Renames and unlinks ride along: they are the same question - "make the folder hold this
        // file under this name" - answered from the file already there instead of from the store.
        var installs = plan.Items
            .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace or ModSyncAction.Rename or ModSyncAction.Unlink)
            .Where(x => stillInPlace.Contains(x.ModId) is false)
            .ToList();

        var completed = 0;

        foreach (var item in installs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ModSyncProgress(ModSyncPhase.Installing, completed, installs.Count)
            {
                ModId = item.ModId.Value,
                Detail = item.DisplayName
            });

            try
            {
                await Task.Run(() => Materialize(plan, item), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordFailure(failures, item, exception);
            }

            completed++;
        }

        progress?.Report(new ModSyncProgress(ModSyncPhase.Installing, completed, installs.Count));
    }

    /// <summary>
    /// Brings every managed file in line with the mods now in the folder. Each edit is recomputed
    /// from the file as it is now, so one the game rewrote since the plan keeps its changes.
    /// </summary>
    private async Task ApplyManagedFilesAsync(ModSyncPlan plan, List<ModSyncFailure> failures, CancellationToken cancellationToken)
    {
        foreach (var managed in plan.ManagedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await Task.Run(() => fileEditor.Apply(plan.ModFolder, managed.Edit), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not update {File} during sync.", managed.FullPath);

                failures.Add(new ModSyncFailure(managed.Edit.RelativePath, exception.Message) { Exception = exception });
            }
        }
    }

    /// <summary>
    /// Makes the mod folder hold this item's file, under the name the adapter says it belongs under
    /// - a second directory entry into the store where that is safe, a copy otherwise, for a rename
    /// just the name, since the right bytes are already there, and for an unlink the name and then a
    /// copy of those bytes over the link.
    /// </summary>
    /// <remarks>
    /// Never replaces another file. The removal phase is the only thing that takes files out of a mod
    /// folder, so a file at the destination is one the plan did not know about, and the install fails
    /// instead.
    /// </remarks>
    private void Materialize(ModSyncPlan plan, ModSyncItem item)
    {
        var destination = Path.Combine(plan.ModFolder, item.FileName!);

        if (item.Action is ModSyncAction.Rename)
        {
            Rename(item.InstalledPath!, destination);

            return;
        }

        if (item.Action is ModSyncAction.Unlink)
        {
            Rename(item.InstalledPath!, destination);
            ReplaceWithCopy(destination);

            return;
        }

        var hash = item.DesiredHash!;
        var blob = plan.ServingStore.GetBlobPath(hash);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            throw new IOException($"'{Path.GetFileName(destination)}' appeared in the mod folder after the plan was made, so it was left alone.");
        }

        if (plan.Materialization.Method is MaterializationMethod.Hardlink &&
            FileLinks.TryCreateHardLink(destination, blob))
        {
            return;
        }

        File.Copy(blob, destination, overwrite: false);

        // A copied file inherits the blob's read-only-ness and timestamps on some paths; make sure
        // the game sees an ordinary, writable file of its own.
        var info = new FileInfo(destination) { IsReadOnly = false };
        info.LastWriteTimeUtc = time.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Gives a hardlinked file data of its own, so nothing written to it afterwards reaches the store.
    /// </summary>
    /// <remarks>
    /// Copied beside it and moved over it, so the name holds the complete file throughout. The link
    /// being replaced is the store blob's other name, so no bytes are lost with it.
    /// </remarks>
    private void ReplaceWithCopy(string path)
    {
        var staging = Path.Combine(Path.GetDirectoryName(path)!, $"{Guid.NewGuid():N}.unlinking");

        try
        {
            File.Copy(path, staging);
            File.Move(staging, path, overwrite: true);
        }
        catch
        {
            FileSystemHelper.TryDeleteFile(staging, logger);

            throw;
        }
    }

    /// <summary>
    /// Gives an already-correct file the name the repo registered for it.
    /// </summary>
    /// <remarks>
    /// A directory operation, so it costs nothing and keeps every hardlink into the blob intact. The
    /// fallback is for the case that motivates the whole action: a rename that only changes case is,
    /// to a case-insensitive filesystem, a move onto a path that already exists, and not every one of
    /// them allows it. Going through a name nothing holds is the same rename in two steps, and the
    /// file is put back under its old name if the second step fails.
    /// </remarks>
    private static void Rename(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.Move(from, to);
        }
        catch (IOException) when (File.Exists(from))
        {
            var staging = Path.Combine(Path.GetDirectoryName(to)!, $"{Guid.NewGuid():N}.renaming");

            File.Move(from, staging);

            try
            {
                File.Move(staging, to);
            }
            catch (Exception)
            {
                File.Move(staging, from);

                throw;
            }
        }
    }

    /// <summary>
    /// Records what is now installed, so the next drift check is a directory listing rather than
    /// 2,000 archives opened - and, first, closes the revision the folder is leaving.
    /// </summary>
    /// <remarks>
    /// <b>The observation goes here rather than at the callers because this is where the revision
    /// moves.</b> The manifest is the only thing that says which mod list this folder runs, so once
    /// it has been rewritten an evening played before the apply is indistinguishable from one played
    /// after, and the savegame's next check-in would name a list it never ran on. Folding the two
    /// together means no path can rewrite the manifest without attributing the play first - the same
    /// by-construction argument the binding store's one-per-slot rule rests on. Nothing is skipped for
    /// a plan that changed no files: a revision can move without a single mod doing so, and the
    /// no-work path rewrites the manifest too.
    /// </remarks>
    public async Task WriteManifestAsync(ModSyncPlan plan)
    {
        // This target's own holds, because this target's manifest is what is about to move: a save in
        // the MP client's savegame folder was played against the MP client's mods, and the dedicated
        // server's apply has nothing to say about it.
        //
        // Deliberately not the caller's token. By this point the folder is already what the profile
        // asked for and the manifest is about to say so; abandoning the attribution here would credit
        // everything played on the outgoing revision to the incoming one, quietly and permanently.
        await playAttribution.ObserveAsync(plan.TargetRef, CancellationToken.None);

        var entries = new List<SyncManifestEntry>();

        foreach (var item in plan.Items.Where(x => x.Action is ModSyncAction.Keep or ModSyncAction.Rename or ModSyncAction.Unlink or ModSyncAction.Install or ModSyncAction.Replace))
        {
            var path = item.Action is ModSyncAction.Keep
                ? item.InstalledPath!
                : Path.Combine(plan.ModFolder, item.FileName!);

            var info = new FileInfo(path);

            if (info.Exists is false)
            {
                continue;
            }

            entries.Add(new SyncManifestEntry(
                item.ModId.Value,
                item.DesiredVersion!.Value.Value,
                item.DesiredHash!,
                info.Name,
                info.Length,
                info.LastWriteTimeUtc)
            {
                Locked = item.Locked,
                DisplayName = item.DisplayName
            });
        }

        manifestStore.Write(new SyncManifest
        {
            Target = plan.TargetRef,
            RepoId = plan.RepoId,
            ProfileId = plan.ProfileId,
            ProfileName = plan.ProfileName,
            ProfileRevision = plan.ProfileRevision,
            SyncedAt = time.GetUtcNow(),
            ModFolder = plan.ModFolder,
            Entries = entries,
            // Only the ones still there: a file that was blocking an install has just been
            // quarantined, and recording it would describe a folder that no longer exists.
            UnmanagedFileNames = [.. plan.UnmanagedFileNames.Where(x => File.Exists(Path.Combine(plan.ModFolder, x)))],
            ManagedFiles = [.. plan.ManagedFiles.Select(x => Path.GetRelativePath(plan.ModFolder, x.FullPath))],
            Shared = plan.Target.Shared
        });
    }

    /// <summary>
    /// Trims the serving store back inside its size limit, never dropping what an active profile on
    /// a disk it serves is relying on.
    /// </summary>
    /// <remarks>
    /// <b>Skipped rather than queued when the store is busy.</b> This is the one exclusive claim in
    /// the app that refuses to wait: it runs at the end of every apply, on a store other applies are
    /// very likely reading, and waiting for them would serialise every sync on the disk - a real cost
    /// to avoid a race that costs one re-download. Everything in a store is registered in a repo and
    /// re-downloadable, so the sweep skipped here is simply the next apply's sweep.
    /// </remarks>
    private ContentStoreEvictionResult? Evict(ModSyncPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            using var lease = leases.TryAcquireExclusive(
                ResourceKeys.Store(plan.ServingStore), $"Tidying the store on {plan.ServingStore.VolumeRoot}");

            if (lease is null)
            {
                logger.LogDebug(
                    "Skipped the post-sync sweep: the store on {Volume} is busy.", plan.ServingStore.VolumeRoot);

                return null;
            }

            return plan.ServingStore.Evict(GetPinnedHashes(plan), CancellationToken.None);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // Housekeeping. A store that could not be swept is a store that is too big, not a failed
            // sync - and the mod folder is already correct by this point.
            logger.LogWarning(exception, "Could not evict from the content store after syncing.");

            return null;
        }
    }

    /// <summary>
    /// Everything an active profile needs on a disk this store serves: this sync's own set, plus what
    /// every other folder's manifest says it is running.
    /// </summary>
    /// <remarks>
    /// <b>The skip is this target, not this game.</b> A game's other targets are other folders
    /// running their own installed sets, and skipping the whole game would evict what the dedicated
    /// server is holding the moment the MP client is synced - a re-download on the next apply of a
    /// folder nobody touched. Only the folder being synced right now is covered by the plan's own
    /// hashes above.
    /// </remarks>
    private IReadOnlySet<string> GetPinnedHashes(ModSyncPlan plan)
    {
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hash in plan.Items.Select(x => x.DesiredHash).OfType<string>())
        {
            pinned.Add(hash);
        }

        foreach (var folder in modFolders.GetAll())
        {
            if (folder.Target == plan.TargetRef)
            {
                continue;
            }

            if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(folder.ModFolder).RootPath, plan.ServingStore.RootPath) is false)
            {
                continue;
            }

            foreach (var entry in manifestStore.TryRead(folder.Target)?.Entries ?? [])
            {
                pinned.Add(entry.ContentHash);
            }
        }

        return pinned;
    }



    /// <summary>
    /// Reports on the calling thread. <see cref="Progress{T}"/> posts to whatever context happened to
    /// be current, which for byte counts arriving thousands of times per file is both slower and out
    /// of order.
    /// </summary>
    private sealed class Forwarder<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>The fetch phase's count and reports, shared by the items fetching at once.</summary>
    /// <remarks>
    /// Reported under a lock, so the count reaches the listener in the order it moved: a report read
    /// just before another item finished would otherwise land just after it and tick the bar back.
    /// </remarks>
    private sealed class FetchRun(int total, IProgress<ModSyncProgress>? progress)
    {
        private readonly Lock _gate = new();

        private int _completed;


        public void Report(ModSyncItem item, long bytesTransferred, long totalBytes)
        {
            lock (_gate)
            {
                progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, _completed, total)
                {
                    ModId = item.ModId.Value,
                    Detail = item.DisplayName,
                    BytesTransferred = bytesTransferred,
                    TotalBytes = totalBytes,
                    Concurrent = true
                });
            }
        }

        public void Finish(ModSyncItem item)
        {
            lock (_gate)
            {
                _completed++;

                progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, _completed, total)
                {
                    ModId = item.ModId.Value,
                    Detail = item.DisplayName,
                    Concurrent = true,
                    ItemFinished = true
                });
            }
        }
    }
}
