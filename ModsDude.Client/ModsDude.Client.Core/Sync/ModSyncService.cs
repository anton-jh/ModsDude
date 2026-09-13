using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Sync;

/// <param name="Target">
/// Which of the game's folders to make match. Sync is genuinely per folder, so this is named by the
/// caller rather than derived here: a game with three targets is three requests, and looping them is
/// what applying a profile does.
/// </param>
/// <param name="Adapter">Already hydrated with the local settings; it is what knows the targets.</param>
public sealed record ModSyncRequest(
    GameIdentity Game,
    ModTarget Target,
    ILocalModAdapter Adapter,
    Guid RepoId,
    Guid ProfileId)
{
    /// <summary>What the manifest for this run is filed under.</summary>
    public ModTargetRef TargetRef => new(Game, Target.Key);

    /// <summary>
    /// What the profile is called, carried into the manifest so a later drift notice can name it
    /// without a repo's profile list to hand. Optional: sync itself has no use for it.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Which revision of the profile to install, or null to let the game's own state decide.
    /// </summary>
    /// <remarks>
    /// <b>Null is the ordinary answer and the one nearly every caller gives.</b> It resolves to the
    /// revision a past savegame held here pins the folder to, and to the profile's head where nothing
    /// pins it - so a re-apply from the drift notice, from the mod list editor and from the game
    /// page all target the right list without any of them knowing what a savegame is. A number is for
    /// the one caller that knows better than the game does: the check-out dialog, previewing the
    /// apply for a savegame this machine is not holding yet.
    /// </remarks>
    public int? Revision { get; init; }
}


/// <summary>The mod folders on this machine, which is what eviction needs to know to spare them.</summary>
/// <remarks>
/// An interface rather than <see cref="GameRepository"/> itself, so the sync engine depends
/// on the one fact it uses and can be exercised without a real <c>state.json</c>.
/// </remarks>
public interface IModFolders
{
    IReadOnlyList<GameModFolder> GetAll();
}

/// <summary>
/// One mod folder on this machine, and which of which game's targets reaches it.
/// </summary>
/// <remarks>
/// One entry per folder, so a game with three targets appears three times: eviction has to spare
/// every folder, and the target is how each one's manifest is found.
/// </remarks>
public sealed record GameModFolder(ModTargetRef Target, string ModFolder);


/// <summary>
/// Makes a game's mod folder contain exactly what a profile pins: plan first, show it, then
/// execute.
/// </summary>
/// <remarks>
/// <para>
/// The execution order is the safety property. The serving store is filled with everything
/// <b>this profile</b> needs before anything in the mod folder is touched, so a failure or a
/// cancellation during the slow part leaves the game exactly as it was, and the destructive phase
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
    IModFolders modFolders,
    IHeldSavegames heldSavegames,
    ILogger<ModSyncService> logger)
{
    /// <summary>Matches the import's, since the repo is expected to hold thousands of versions.</summary>
    private const int _registeredPageSize = 500;


    /// <param name="progress">
    /// Where to report which mod is being examined. Optional, and worth passing: on a folder whose
    /// files no longer match the manifest this reads and hashes every one of them, which is the
    /// slowest thing an apply does and used to happen with nothing at all on screen.
    /// </param>
    public async Task<ModSyncPlan> PlanAsync(
        ModSyncRequest request,
        CancellationToken cancellationToken,
        IProgress<ModSyncProgress>? progress = null)
    {
        var target = request.Target;
        var modFolder = target.Path;

        if (Directory.Exists(modFolder) is false)
        {
            throw new UserFriendlyException(
                "That mod folder is not reachable",
                $"'{modFolder}' does not exist right now. An unplugged drive or an offline network path looks like this; nothing has been changed.");
        }

        var targetRevision = ResolveTargetRevision(request);
        var (desired, revision) = await GetDesiredAsync(request, targetRevision, cancellationToken);
        var installed = await GetInstalledAsync(request.Adapter, target, cancellationToken);
        var manifest = manifestStore.TryRead(request.TargetRef);

        // Fetched only when something is actually going to be removed. It is the one input that
        // needs the repo's mod list, and a re-apply that changes nothing should not pay for it.
        var registered = NeedsRegisteredContent(desired, installed.Mods, manifest)
            ? await GetRegisteredContentAsync(request.RepoId, cancellationToken)
            : RegisteredContent.None;

        var items = await ModSyncPlanner.PlanAsync(
            desired, installed.Mods, registered, manifest, null, cancellationToken, progress);

        var servingStore = storeProvider.GetStoreServing(modFolder);
        var allStores = storeProvider.GetAllStores();

        items = [.. items, .. FindBlockingFiles(items, installed.UnmanagedFileNames, target, request.Adapter)];

        return new ModSyncPlan
        {
            RepoId = request.RepoId,
            ProfileId = request.ProfileId,
            ProfileName = request.ProfileName,
            ProfileRevision = revision,
            Game = request.Game,
            Target = target,
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
            // Nothing in the mod folder has been touched, so stopping here leaves the game
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
            await WriteManifestAsync(plan);
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
                // Collected rather than thrown, so the rest of the sync still runs - which is also
                // why nothing else would ever see the stack.
                logger.LogError(exception, "{Action} failed for {Mod} during sync.", item.Action, item.ModId.Value);

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
                // Collected rather than thrown, so the rest of the sync still runs - which is also
                // why nothing else would ever see the stack.
                logger.LogError(exception, "{Action} failed for {Mod} during sync.", item.Action, item.ModId.Value);

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
        catch (Exception exception)
        {
            // Both routes refused. The file stays where it is, which is the safe end of the failure -
            // sync reports it rather than removing something it cannot put back.
            logger.LogWarning(exception, "Could not displace {File} for {Mod}; leaving it where it is.", path, item.ModId.Value);

            return new QuarantinedFile(item.ModId, path, QuarantineDestination.Failed);
        }
    }

    private async Task InstallAsync(
        ModSyncPlan plan,
        IProgress<ModSyncProgress>? progress,
        List<ModSyncFailure> failures,
        CancellationToken cancellationToken)
    {
        // Renames ride along: they are the same question - "make the folder hold this file under
        // this name" - answered with one directory operation instead of a copy.
        var installs = plan.Items
            .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace or ModSyncAction.Rename)
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
                // Collected rather than thrown, so the rest of the sync still runs - which is also
                // why nothing else would ever see the stack.
                logger.LogError(exception, "{Action} failed for {Mod} during sync.", item.Action, item.ModId.Value);

                failures.Add(new ModSyncFailure(item.ModId, item.Action, exception.Message) { Exception = exception });
            }

            completed++;
        }

        progress?.Report(new ModSyncProgress(ModSyncPhase.Installing, completed, installs.Count));
    }

    /// <summary>
    /// Makes the mod folder hold this item's file, under the name the adapter says it belongs under
    /// - a second directory entry into the store where that is safe, a copy otherwise, and for a
    /// rename just the name, since the right bytes are already there.
    /// </summary>
    private static void Materialize(ModSyncPlan plan, ModSyncItem item)
    {
        var destination = plan.Adapter.GetModFilePath(plan.Target, item.ModId, item.DesiredVersion!.Value, item.FileName);

        if (item.Action is ModSyncAction.Rename)
        {
            Rename(item.InstalledPath!, destination);

            return;
        }

        var hash = item.DesiredHash!;
        var blob = plan.ServingStore.GetBlobPath(hash);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            // The removal phase took the old file, so anything still here is a leftover of a failed
            // run rather than something the user owns - a plan that found a file it does not
            // recognise here would have listed it for quarantine.
            File.Delete(destination);
        }

        if (plan.Materialization.Method is MaterializationMethod.Hardlink &&
            FileLinks.TryCreateHardLink(destination, blob))
        {
            return;
        }

        File.Copy(blob, destination);

        // A copied file inherits the blob's read-only-ness and timestamps on some paths; make sure
        // the game sees an ordinary, writable file of its own.
        var info = new FileInfo(destination) { IsReadOnly = false };
        info.LastWriteTimeUtc = DateTime.UtcNow;
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
    /// Records a folder that already matches its profile, without touching a file in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Nothing to do" and "nothing to record" are not the same thing. A mod dropped into the folder
    /// by hand and then imported and pinned leaves the folder correct and the <em>manifest</em>
    /// stale: the file is not in it, so every drift check goes on reporting it as added. Applying the
    /// profile is exactly when the user has said the folder is what they want, so that is when the
    /// record catches up - otherwise the notice can never be cleared by the button it offers.
    /// </para>
    /// <para>
    /// Only for a plan with no work in it. A plan that has work has to be executed, and executing it
    /// writes the manifest itself.
    /// </para>
    /// </remarks>
    public async Task RecordAlreadyMatchedAsync(ModSyncPlan plan)
    {
        if (plan.HasWork)
        {
            throw new InvalidOperationException(
                "A plan with work in it has to be executed; executing it is what records the result.");
        }

        await WriteManifestAsync(plan);
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
    private async Task WriteManifestAsync(ModSyncPlan plan)
    {
        // This target's own holds, because this target's manifest is what is about to move: a save in
        // the MP client's savegame folder was played against the MP client's mods, and the dedicated
        // server's apply has nothing to say about it.
        //
        // Deliberately not the caller's token. By this point the folder is already what the profile
        // asked for and the manifest is about to say so; abandoning the attribution here would credit
        // everything played on the outgoing revision to the incoming one, quietly and permanently.
        await heldSavegames.ObserveAsync(plan.TargetRef, CancellationToken.None);

        var entries = new List<SyncManifestEntry>();

        foreach (var item in plan.Items.Where(x => x.Action is ModSyncAction.Keep or ModSyncAction.Rename or ModSyncAction.Install or ModSyncAction.Replace))
        {
            var path = item.Action is ModSyncAction.Keep
                ? item.InstalledPath!
                : plan.Adapter.GetModFilePath(plan.Target, item.ModId, item.DesiredVersion!.Value, item.FileName);

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
    /// Which revision this game's folder is to end up on, refusing the apply outright where a
    /// savegame it is holding says it may not move at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resolved here rather than at each caller, for the reason the observation is.</b> Every apply
    /// in the app reaches this method, and an apply that quietly installs head under a savegame pinned to
    /// revision 4 is the state the whole design exists to prevent - so the one place all of them pass
    /// through is where the question gets asked. A caller that names a revision has said something
    /// this cannot know better than, and is taken at its word and then checked.
    /// </para>
    /// <para>
    /// The refusal is a backstop and not the explanation. An interface that offers a button and then
    /// throws this has already failed; it consults the same rule beforehand and says what would enable
    /// the apply instead - see docs/10-savegame-profile-binding.md#two-actions-not-one.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException">A savegame held here refuses this apply.</exception>
    private int? ResolveTargetRevision(ModSyncRequest request)
    {
        var revision = request.Revision ?? heldSavegames.GetRequiredRevision(request.Game, request.ProfileId);
        var decision = heldSavegames.DecideApply(request.Game, request.ProfileId, revision);

        return decision.Refusal switch
        {
            SavegameApplyRefusal.None => revision,

            SavegameApplyRefusal.AnotherProfileIsHeld => throw new UserFriendlyException(
                "A savegame checked out here follows another mod list",
                $"Game '{request.Game}' is holding savegame '{decision.SavegameId}', which follows profile '{decision.ProfileId}'. Applying '{request.ProfileId}' would take that savegame off the mod list it runs on. Check it in first."),

            _ => throw new UserFriendlyException(
                "That savegame runs on one revision, and this is not it",
                $"Game '{request.Game}' is holding savegame '{decision.SavegameId}', a past savegame pinned to revision {decision.Revision} of profile '{decision.ProfileId}'. Revision {revision} was asked for; only {decision.Revision} may be applied while it is held.")
        };
    }

    /// <summary>
    /// What the profile pins at the requested revision, and which revision that is - recorded in the
    /// manifest so a folder can say which version of the list it was made to match.
    /// </summary>
    /// <remarks>
    /// The revision is answered back rather than assumed from what was asked, because null means head
    /// and only the server knows which number that is.
    /// </remarks>
    private async Task<(IReadOnlyList<DesiredMod> Mods, int Revision)> GetDesiredAsync(
        ModSyncRequest request, int? revision, CancellationToken cancellationToken)
    {
        var response = await modDependenciesClient.GetModDependenciesV1Async(request.RepoId, request.ProfileId, revision, cancellationToken);

        // Normalized where the ids enter the client, as everywhere else, and the file name checked
        // in the same breath: it came off somebody else's disk and is about to be interpolated into
        // a path here, so it is validated against the id it arrived with rather than trusted.
        return ([.. response.Dependencies.Select(x =>
        {
            var modId = ModKey.From(x.ModId);

            return new DesiredMod(
                modId,
                ModVersionKey.From(x.ModVersionId),
                x.ContentHash,
                x.Locked)
            {
                FileName = ModFileName.For(modId, x.FileName)
            };
        })], response.Revision);
    }

    private static async Task<(IReadOnlyList<InstalledMod> Mods, IReadOnlyList<string> UnmanagedFileNames)> GetInstalledAsync(
        ILocalModAdapter adapter,
        ModTarget target,
        CancellationToken cancellationToken)
    {
        var found = await adapter.GetInstalledMods(target, cancellationToken);
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
        var unmanaged = Directory.EnumerateFiles(target.Path)
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
        ModTarget target,
        ILocalModAdapter adapter)
    {
        if (unmanagedFileNames.Count == 0)
        {
            return [];
        }

        var unmanaged = new HashSet<string>(unmanagedFileNames, StringComparer.OrdinalIgnoreCase);
        var blocking = new List<ModSyncItem>();

        foreach (var item in items.Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace or ModSyncAction.Rename))
        {
            var name = Path.GetFileName(adapter.GetModFilePath(target, item.ModId, item.DesiredVersion!.Value, item.FileName));

            if (unmanaged.Remove(name) is false)
            {
                continue;
            }

            blocking.Add(new ModSyncItem
            {
                Action = ModSyncAction.Quarantine,
                ModId = item.ModId,
                DisplayName = name,
                InstalledPath = Path.Combine(target.Path, name),
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
    private ModMaterialization DecideMaterialization(string modFolder, ContentStore servingStore, ILocalModAdapter adapter)
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

    private bool SupportsHardlinks(ContentStore servingStore)
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
        catch (Exception exception)
        {
            // A store that cannot be hardlinked into is copied into instead, which is slower and
            // correct - and indistinguishable from a store that simply chose to copy.
            logger.LogInformation(exception, "Hardlink probe failed in {Store}; falling back to copying.", servingStore.RootPath);

            return false;
        }
        finally
        {
            TryDelete(link);
            TryDelete(probe);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            // A leftover probe file is inert.
            logger.LogDebug(exception, "Could not delete the hardlink probe file {File}.", path);
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
