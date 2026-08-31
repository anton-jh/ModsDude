using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Sync;

/// <param name="Adapter">Already hydrated with the instance's settings; it is what knows the mod folder.</param>
public sealed record ModSyncRequest(Guid InstanceId, IInstanceModAdapter Adapter, Guid RepoId, Guid ProfileId)
{
    /// <summary>
    /// What the profile is called, carried into the manifest so a later drift notice can name it
    /// without a repo's profile list to hand. Optional: sync itself has no use for it.
    /// </summary>
    public string? ProfileName { get; init; }
}


/// <summary>The mod folders on this machine, which is what eviction needs to know to spare them.</summary>
/// <remarks>
/// An interface rather than <see cref="LocalInstanceRepository"/> itself, so the sync engine depends
/// on the one fact it uses and can be exercised without a real <c>state.json</c>.
/// </remarks>
public interface IInstanceModFolders
{
    IReadOnlyList<InstanceModFolder> GetAll();
}

public sealed record InstanceModFolder(Guid InstanceId, string ModFolder);


/// <summary>
/// Makes an instance's mod folder contain exactly what a profile pins: plan first, show it, then
/// execute.
/// </summary>
/// <remarks>
/// <para>
/// The execution order is the safety property. The serving store is filled with everything
/// <b>this profile</b> needs before anything in the mod folder is touched, so a failure or a
/// cancellation during the slow part leaves the instance exactly as it was, and the destructive phase
/// only ever runs against a store that already holds what the profile needs. There is no prefetching
/// of the repo's full mod set - at thousands of registered versions that is tens of gigabytes for
/// content the user may never activate.
/// </para>
/// <para>
/// Everything reports per mod. Two thousand files is minutes of work even on the fast path, and a
/// frozen progress bar is indistinguishable from a hang.
/// </para>
/// </remarks>
public sealed class ModSyncService(
    IModDependenciesClient modDependenciesClient,
    IModsClient modsClient,
    IFilesClient filesClient,
    IModFileDownloader downloader,
    IContentStoreProvider storeProvider,
    SyncManifestStore manifestStore,
    IRecycleBin recycleBin,
    IInstanceModFolders instanceModFolders)
{
    /// <summary>Matches the import's, since the repo is expected to hold thousands of versions.</summary>
    private const int _registeredPageSize = 500;


    public async Task<ModSyncPlan> PlanAsync(ModSyncRequest request, CancellationToken cancellationToken)
    {
        var modFolder = request.Adapter.ModFolder;

        if (Directory.Exists(modFolder) is false)
        {
            throw new UserFriendlyException(
                "That mod folder is not reachable",
                $"'{modFolder}' does not exist right now. An unplugged drive or an offline network path looks like this; nothing has been changed.");
        }

        var desired = await GetDesiredAsync(request, cancellationToken);
        var installed = await GetInstalledAsync(request.Adapter, cancellationToken);
        var manifest = manifestStore.TryRead(request.InstanceId);

        // Fetched only when something is actually going to be removed. It is the one input that
        // needs the repo's mod list, and a re-apply that changes nothing should not pay for it.
        var registered = NeedsRegisteredContent(desired, installed.Mods, manifest)
            ? await GetRegisteredContentAsync(request.RepoId, cancellationToken)
            : RegisteredContent.None;

        var items = await ModSyncPlanner.PlanAsync(desired, installed.Mods, registered, manifest, null, cancellationToken);

        var servingStore = storeProvider.GetStoreServing(modFolder);
        var allStores = storeProvider.GetAllStores();

        items = [.. items, .. FindBlockingFiles(items, installed.UnmanagedFileNames, modFolder, request.Adapter)];

        return new ModSyncPlan
        {
            RepoId = request.RepoId,
            ProfileId = request.ProfileId,
            ProfileName = request.ProfileName,
            InstanceId = request.InstanceId,
            ModFolder = modFolder,
            Items = items,
            Materialization = DecideMaterialization(modFolder, servingStore, request.Adapter),
            UnmanagedFileNames = installed.UnmanagedFileNames,
            HashesToFetch = [.. items
                .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace)
                .Select(x => x.DesiredHash)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => servingStore.Contains(x) is false)],
            Adapter = request.Adapter,
            ServingStore = servingStore,
            AllStores = allStores.Any(x => FileSystemHelper.ArePathsEqual(x.RootPath, servingStore.RootPath))
                ? allStores
                : [servingStore, .. allStores]
        };
    }

    public async Task<ModSyncResult> ExecuteAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var failures = new List<ModSyncFailure>();

        await FetchAsync(plan, progress, failures, cancellationToken);

        if (failures.Count > 0)
        {
            // Nothing in the mod folder has been touched, so stopping here leaves the instance
            // exactly as it was rather than half-applied.
            return new ModSyncResult(false, failures);
        }

        var quarantined = new List<QuarantinedFile>();

        await RemoveAsync(plan, progress, failures, quarantined, cancellationToken);
        await InstallAsync(plan, progress, failures, cancellationToken);

        var completed = failures.Count == 0;

        progress?.Report(new ModSyncProgress(ModSyncPhase.Finishing, 0, 1));

        if (completed)
        {
            WriteManifest(plan);
        }

        var eviction = Evict(plan, cancellationToken);

        return new ModSyncResult(completed, failures)
        {
            Quarantined = quarantined,
            Eviction = eviction,
            ManifestWritten = completed
        };
    }


    /// <summary>
    /// Fills the serving store: another disk's store first, the network second. A disk-to-disk copy
    /// beats a download every time and leaves the blob local for the next install to this disk.
    /// </summary>
    private async Task FetchAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        List<ModSyncFailure> failures,
        CancellationToken cancellationToken)
    {
        var wanted = plan.Items
            .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace)
            .Where(x => x.DesiredHash is not null && plan.ServingStore.Contains(x.DesiredHash) is false)
            .GroupBy(x => x.DesiredHash!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var completed = 0;

        foreach (var group in wanted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = group.First();

            progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, completed, wanted.Count)
            {
                ModId = item.ModId.Value,
                Detail = item.DisplayName
            });

            try
            {
                await FetchOneAsync(plan, item, group.Key, completed, wanted.Count, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new ModSyncFailure(item.ModId, item.Action, exception.Message) { Exception = exception });
            }

            completed++;
        }

        progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, completed, wanted.Count));
    }

    private async Task FetchOneAsync(
        ModSyncPlan plan,
        ModSyncItem item,
        string hash,
        int completed,
        int total,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var elsewhere = plan.AllStores.FirstOrDefault(x =>
            FileSystemHelper.ArePathsEqual(x.RootPath, plan.ServingStore.RootPath) is false &&
            x.Contains(hash));

        // Known up front for a cross-store copy, and only once the response headers arrive for a
        // download - so the row can show a proportion in both cases rather than only one.
        var totalBytes = elsewhere?.GetSize(hash) ?? 0;

        var report = new Forwarder<long>(x => progress?.Report(
            new ModSyncProgress(ModSyncPhase.Fetching, completed, total)
            {
                ModId = item.ModId.Value,
                Detail = item.DisplayName,
                BytesTransferred = x,
                TotalBytes = totalBytes
            }));

        if (elsewhere is not null)
        {
            await plan.ServingStore.CopyFromAsync(elsewhere, hash, report, cancellationToken);

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

        using var download = await downloader.OpenAsync(link.Link, cancellationToken);

        totalBytes = download.Length ?? 0;

        // Verified against what the repo declared before it is stored, never after. This is the
        // check that makes a store shared between repos safe; see ContentStore.IngestAsync.
        await plan.ServingStore.IngestAsync(download.Content, hash, report, cancellationToken);
    }

    /// <summary>
    /// The destructive phase, under the uninstall rules: a file whose bytes the repo can reproduce is
    /// deleted once some store holds them, and anything else goes to the Recycle Bin.
    /// </summary>
    private async Task RemoveAsync(
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

        var runStartedAt = DateTimeOffset.UtcNow;
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
                if (item.InstalledIsRecoverable && item.InstalledHash is string hash)
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
                failures.Add(new ModSyncFailure(item.ModId, item.Action, exception.Message) { Exception = exception });
            }

            completed++;
        }

        progress?.Report(new ModSyncProgress(ModSyncPhase.Removing, completed, removals.Count));
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

        if (recycleBin.IsAvailableFor(path) && recycleBin.TryRecycle(path))
        {
            return new QuarantinedFile(item.ModId, path, QuarantineDestination.RecycleBin);
        }

        // A drive with the Recycle Bin turned off, or a network path. The file is still not deleted;
        // it moves into the store's quarantine folder and the UI says where it went.
        try
        {
            var directory = plan.ServingStore.GetQuarantineDirectory(runStartedAt);
            Directory.CreateDirectory(directory);

            var destination = Path.Combine(directory, Path.GetFileName(path));

            File.Move(path, destination, overwrite: true);

            return new QuarantinedFile(item.ModId, path, QuarantineDestination.QuarantineFolder) { Path = destination };
        }
        catch (Exception)
        {
            // Both routes refused. The file stays where it is, which is the safe end of the failure -
            // sync reports it rather than removing something it cannot put back.
            return new QuarantinedFile(item.ModId, path, QuarantineDestination.Failed);
        }
    }

    private async Task InstallAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        List<ModSyncFailure> failures,
        CancellationToken cancellationToken)
    {
        var installs = plan.Items
            .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace)
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
                failures.Add(new ModSyncFailure(item.ModId, item.Action, exception.Message) { Exception = exception });
            }

            completed++;
        }

        progress?.Report(new ModSyncProgress(ModSyncPhase.Installing, completed, installs.Count));
    }

    /// <summary>
    /// Puts the store's bytes where the adapter says the file belongs - a second directory entry
    /// where that is safe, a copy otherwise.
    /// </summary>
    private static void Materialize(ModSyncPlan plan, ModSyncItem item)
    {
        var hash = item.DesiredHash!;
        var target = plan.Adapter.GetModFilePath(item.ModId, item.DesiredVersion!.Value);
        var blob = plan.ServingStore.GetBlobPath(hash);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        if (File.Exists(target))
        {
            // The removal phase took the old file, so anything still here is a leftover of a failed
            // run rather than something the user owns - a plan that found a file it does not
            // recognise here would have listed it for quarantine.
            File.Delete(target);
        }

        if (plan.Materialization.Method is MaterializationMethod.Hardlink &&
            FileLinks.TryCreateHardLink(target, blob))
        {
            return;
        }

        File.Copy(blob, target);

        // A copied file inherits the blob's read-only-ness and timestamps on some paths; make sure
        // the game sees an ordinary, writable file of its own.
        var info = new FileInfo(target) { IsReadOnly = false };
        info.LastWriteTimeUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Records what is now installed, so the next drift check is a directory listing rather than
    /// 2,000 archives opened.
    /// </summary>
    private void WriteManifest(ModSyncPlan plan)
    {
        var entries = new List<SyncManifestEntry>();

        foreach (var item in plan.Items.Where(x => x.Action is ModSyncAction.Keep or ModSyncAction.Install or ModSyncAction.Replace))
        {
            var path = item.Action is ModSyncAction.Keep
                ? item.InstalledPath!
                : plan.Adapter.GetModFilePath(item.ModId, item.DesiredVersion!.Value);

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
            InstanceId = plan.InstanceId,
            RepoId = plan.RepoId,
            ProfileId = plan.ProfileId,
            ProfileName = plan.ProfileName,
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = plan.ModFolder,
            Entries = entries,
            // Only the ones still there: a file that was blocking an install has just been
            // quarantined, and recording it would describe a folder that no longer exists.
            UnmanagedFileNames = [.. plan.UnmanagedFileNames.Where(x => File.Exists(Path.Combine(plan.ModFolder, x)))]
        });
    }

    /// <summary>
    /// Trims the serving store back inside its size limit, never dropping what an active profile on
    /// a disk it serves is relying on.
    /// </summary>
    private ContentStoreEvictionResult? Evict(ModSyncPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            return plan.ServingStore.Evict(GetPinnedHashes(plan), CancellationToken.None);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // Housekeeping. A store that could not be swept is a store that is too big, not a failed
            // sync - and the mod folder is already correct by this point.
            return null;
        }
    }

    /// <summary>
    /// Everything an active profile needs on a disk this store serves: this sync's own set, plus what
    /// the other instances' manifests say they are running.
    /// </summary>
    private IReadOnlySet<string> GetPinnedHashes(ModSyncPlan plan)
    {
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hash in plan.Items.Select(x => x.DesiredHash).OfType<string>())
        {
            pinned.Add(hash);
        }

        foreach (var instance in instanceModFolders.GetAll())
        {
            if (instance.InstanceId == plan.InstanceId)
            {
                continue;
            }

            if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(instance.ModFolder).RootPath, plan.ServingStore.RootPath) is false)
            {
                continue;
            }

            foreach (var entry in manifestStore.TryRead(instance.InstanceId)?.Entries ?? [])
            {
                pinned.Add(entry.ContentHash);
            }
        }

        return pinned;
    }


    private async Task<IReadOnlyList<DesiredMod>> GetDesiredAsync(ModSyncRequest request, CancellationToken cancellationToken)
    {
        var dependencies = await modDependenciesClient.GetModDependenciesV1Async(request.RepoId, request.ProfileId, cancellationToken);

        // Normalized where the ids enter the client, as everywhere else: the server holds whatever
        // casing was registered, and an un-normalized id would miss the file it belongs to.
        return [.. dependencies.Select(x => new DesiredMod(
            ModKey.From(x.ModId),
            ModVersionKey.From(x.ModVersionId),
            x.ContentHash,
            x.Locked))];
    }

    private static async Task<(IReadOnlyList<InstalledMod> Mods, IReadOnlyList<string> UnmanagedFileNames)> GetInstalledAsync(
        IInstanceModAdapter adapter,
        CancellationToken cancellationToken)
    {
        var found = await adapter.GetInstalledMods(cancellationToken);
        var mods = new List<InstalledMod>();
        var recognised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in found)
        {
            var path = adapter.GetInstalledModPath(mod);
            var info = new FileInfo(path);

            if (info.Exists is false)
            {
                continue;
            }

            recognised.Add(info.Name);
            mods.Add(new InstalledMod(mod.Id, mod.Version, path, mod.Name, info.Length, info.LastWriteTimeUtc));
        }

        // Everything else in the folder is somebody else's business - a readme, a log, an archive
        // that is not a mod. Recorded so that drift detection does not report them as additions
        // forever, and otherwise never touched.
        var unmanaged = Directory.EnumerateFiles(adapter.ModFolder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(x => recognised.Contains(x) is false)
            .ToList();

        return (mods, unmanaged);
    }

    /// <summary>
    /// Files the adapter does not recognise that sit exactly where a mod is about to be installed -
    /// a corrupt download under the right name is the ordinary way this happens. They are the user's
    /// files, so they are quarantined rather than overwritten.
    /// </summary>
    private static IReadOnlyList<ModSyncItem> FindBlockingFiles(
        IReadOnlyList<ModSyncItem> items,
        IReadOnlyList<string> unmanagedFileNames,
        string modFolder,
        IInstanceModAdapter adapter)
    {
        if (unmanagedFileNames.Count == 0)
        {
            return [];
        }

        var unmanaged = new HashSet<string>(unmanagedFileNames, StringComparer.OrdinalIgnoreCase);
        var blocking = new List<ModSyncItem>();

        foreach (var item in items.Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace))
        {
            var name = Path.GetFileName(adapter.GetModFilePath(item.ModId, item.DesiredVersion!.Value));

            if (unmanaged.Remove(name) is false)
            {
                continue;
            }

            blocking.Add(new ModSyncItem
            {
                Action = ModSyncAction.Quarantine,
                ModId = item.ModId,
                DisplayName = name,
                InstalledPath = Path.Combine(modFolder, name),
                InstalledIsRecoverable = false
            });
        }

        return blocking;
    }

    /// <summary>
    /// Whether anything is going to be removed, which is the only thing the repo's mod list is needed
    /// for. Answered from the manifest, using exactly the check the planner uses, so the two cannot
    /// disagree about which files match.
    /// </summary>
    private static bool NeedsRegisteredContent(
        IReadOnlyCollection<DesiredMod> desired,
        IReadOnlyCollection<InstalledMod> installed,
        SyncManifest? manifest)
    {
        if (installed.Count == 0)
        {
            return false;
        }

        var wanted = desired.ToDictionary(x => x.ModId);
        var recorded = (manifest?.Entries ?? []).ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in installed)
        {
            if (wanted.TryGetValue(mod.ModId, out var want) is false)
            {
                return true;
            }

            if (recorded.TryGetValue(Path.GetFileName(mod.Path), out var entry) is false ||
                entry.Size != mod.Size ||
                entry.ModifiedUtc != mod.ModifiedUtc ||
                ModContentHasher.Matches(entry.ContentHash, want.ContentHash) is false)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<RegisteredContent> GetRegisteredContentAsync(Guid repoId, CancellationToken cancellationToken)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        do
        {
            var page = await modsClient.GetModsV1Async(repoId, null, cursor, _registeredPageSize, cancellationToken);

            foreach (var mod in page.Mods)
            {
                hashes.Add(mod.ContentHash);
            }

            cursor = page.NextCursor;
        }
        while (string.IsNullOrEmpty(cursor) is false);

        return new RegisteredContent(hashes);
    }

    /// <summary>
    /// Hardlink where the disk is served by its own store and the adapter says the game's updater
    /// will not rewrite a mod file in place; a copy in every other case.
    /// </summary>
    /// <remarks>
    /// The fallback is probed rather than assumed, in the store's own temporary folder - same volume,
    /// therefore same filesystem, and nothing is written into the folder the game owns. Only a
    /// same-disk assignment that falls back is worth warning about: a cross-disk store is a
    /// deliberate trade of sync time for space, and an adapter without hardlink support is a stated
    /// property of the game rather than a silent surprise.
    /// </remarks>
    private static ModMaterialization DecideMaterialization(string modFolder, ContentStore servingStore, IInstanceModAdapter adapter)
    {
        var sameVolume = string.Equals(
            FileSystemHelper.NormalizeVolumeRoot(modFolder),
            FileSystemHelper.NormalizeVolumeRoot(servingStore.RootPath),
            StringComparison.OrdinalIgnoreCase);

        if (sameVolume is false || adapter.SupportsHardlinks is false)
        {
            return new ModMaterialization(MaterializationMethod.Copy, FellBackToCopy: false);
        }

        return SupportsHardlinks(servingStore)
            ? new ModMaterialization(MaterializationMethod.Hardlink, FellBackToCopy: false)
            : new ModMaterialization(MaterializationMethod.Copy, FellBackToCopy: true);
    }

    private static bool SupportsHardlinks(ContentStore servingStore)
    {
        var directory = Path.Combine(servingStore.RootPath, "tmp");
        var probe = Path.Combine(directory, $"linkprobe-{Guid.NewGuid():N}");
        var link = probe + ".link";

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(probe, []);

            return FileLinks.TryCreateHardLink(link, probe);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            TryDelete(link);
            TryDelete(probe);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A leftover probe file is inert.
        }
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
}
