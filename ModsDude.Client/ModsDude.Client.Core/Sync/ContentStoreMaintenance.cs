using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

/// <param name="ModFolder">The folder still holding the bad bytes, whatever the store now says.</param>
/// <param name="ProfileName">What the manifest recorded was applied there, where it recorded one.</param>
/// <param name="Mods">How many of the failed addresses that folder is running.</param>
public sealed record AffectedModFolder(string ModFolder, string? ProfileName, int Mods);

/// <param name="Affected">
/// The mod folders that need re-applying. Empty is the good answer and the usual one - a corrupt
/// blob nothing was running is repaired completely by dropping it.
/// </param>
public sealed record StoreVerificationReport(
    ContentStoreVerificationResult Result,
    IReadOnlyList<AffectedModFolder> Affected);

/// <summary>
/// Everything done to a content store that is not a sync: keeping it inside its limit, checking it is
/// still what it claims to be, and giving its space back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The split here is who asked.</b> <see cref="SweepAllAsync"/> is the app keeping the promise the
/// size limit makes, so it runs on its own and skips anything busy; the rest are things a person
/// pressed, so they wait, they report, and they ask first where the answer is not obvious.
/// </para>
/// <para>
/// The user-facing half exists because a store fills a disk quietly, and because the automatic half
/// cannot reach everything on its own - a store nothing serves any more is the one somebody is most
/// likely to want gone, and no sync will ever visit it.
/// See docs/07-mod-sync-design.md#store-eviction-and-the-size-limit.
/// </para>
/// <para>
/// Nothing here can lose data: every blob is registered in some repo and re-downloadable, which is
/// the same property eviction leans on. The one exception is the quarantine folder, which is why
/// <see cref="ContentStore.ClearQuarantine"/> is a separate act with its own question.
/// </para>
/// </remarks>
public sealed class ContentStoreMaintenance(
    IContentStoreProvider storeProvider,
    IModFolders modFolders,
    SyncManifestStore manifestStore,
    IResourceLeases leases,
    ILogger<ContentStoreMaintenance> logger)
{
    /// <summary>
    /// Every store on this machine: the ones serving a mod folder now, and the ones settings still
    /// name.
    /// </summary>
    /// <remarks>
    /// Both halves are needed and neither is a superset of the other.
    /// <see cref="IContentStoreProvider.GetAllStores"/> reads settings, so it misses the store a
    /// mod folder is served by under the defaults nobody has visited a page to accept; and a store
    /// whose disk no longer holds any game is exactly the one worth being able to empty, so it
    /// cannot be dropped for having nothing to serve.
    /// </remarks>
    public IReadOnlyList<ContentStore> GetStores()
    {
        var serving = modFolders.GetAll()
            .Select(x => x.ModFolder)
            .Select(storeProvider.GetStoreServing);

        return [.. serving
            .Concat(storeProvider.GetAllStores())
            .DistinctBy(x => FileSystemHelper.NormalizePathForComparison(x.RootPath))
            .OrderBy(x => x.VolumeRoot, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Trims every store on this machine back inside its size limit. Nobody asks for this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keeping a store inside the size limit is the app's job, not the user's.</b> A limit somebody
    /// typed once is a promise the app makes, and a button labelled "trim" is that promise handed back
    /// to them to keep - which also means it is only kept by the users who noticed the button.
    /// </para>
    /// <para>
    /// The sweep at the end of every apply does most of it, but it only ever touches the store that
    /// apply was using, so three gaps were left standing: a store that no longer serves any mod folder
    /// is never swept at all, an import seeds bytes into a store without any sync following it, and a
    /// limit lowered in settings does nothing until the next apply happens to that disk. This is what
    /// closes them, and it runs on the events that open them - startup, a finished import, a saved
    /// settings page.
    /// </para>
    /// <para>
    /// <b>Skips a busy store rather than waiting for it</b>, exactly as the sync's own sweep does and
    /// for the same reason: nobody is watching this, so blocking an apply to reclaim space a moment
    /// sooner is the wrong trade. Everything in a store is registered in a repo and re-downloadable,
    /// so a store missed here is swept by the next trigger.
    /// </para>
    /// <para>
    /// What each served game is running comes off its sync manifest, so this asks the network nothing
    /// and works offline.
    /// </para>
    /// </remarks>
    /// <returns>How many bytes were given back, across every store that needed it.</returns>
    public async Task<long> SweepAllAsync(CancellationToken cancellationToken)
    {
        long reclaimed = 0;

        foreach (var store in GetStores())
        {
            cancellationToken.ThrowIfCancellationRequested();

            reclaimed += SweepIfIdle(store, cancellationToken);
        }

        // Nothing awaits inside the loop - the claim is a try and the evict is synchronous - but the
        // signature stays asynchronous because every caller is firing this off the UI thread and
        // would otherwise have to remember to.
        return await Task.FromResult(reclaimed);
    }

    /// <summary>One store, if nothing else is using it. Returns the bytes it gave back.</summary>
    private long SweepIfIdle(ContentStore store, CancellationToken cancellationToken)
    {
        try
        {
            using var lease = leases.TryAcquireExclusive(
                ResourceKeys.Store(store), $"Tidying the store on {store.VolumeRoot}");

            if (lease is null)
            {
                logger.LogDebug("Skipped tidying the store on {Volume}: it is busy.", store.VolumeRoot);

                return 0;
            }

            var result = store.Evict(GetPinnedHashes(store), cancellationToken);

            if (result.EntriesEvicted > 0)
            {
                logger.LogInformation(
                    "Tidied the store on {Volume}: dropped {Entries} files and freed {Bytes} bytes.",
                    store.VolumeRoot, result.EntriesEvicted, result.BytesReclaimed);
            }

            return result.BytesReclaimed;
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // A store that could not be tidied is a store that is too big, which is not worth failing
            // whatever triggered this. The next trigger tries again.
            logger.LogWarning(exception, "Could not tidy the store on {Volume}.", store.VolumeRoot);

            return 0;
        }
    }

    /// <summary>
    /// Gives back everything a store is costing, keeping what the mod folders it serves are running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the same as emptying it</b> - see <see cref="ContentStore.Reclaim"/>. What a mod folder
    /// on this disk already holds costs the store no bytes of its own, so dropping it would free
    /// nothing and only guarantee a re-download.
    /// </para>
    /// <para>
    /// <b>Waits for the store, where <see cref="SweepAllAsync"/> skips it.</b> The difference is who
    /// asked: tidying is housekeeping nobody requested and the next trigger will do it anyway, whereas
    /// somebody pressing this is waiting for an answer, and refusing them on the grounds that a sync
    /// is running would only send them back to press it again. <paramref name="onWaiting"/> is how
    /// that wait gets said out loud.
    /// </para>
    /// </remarks>
    public async Task<ContentStoreClearResult> ReclaimAsync(
        ContentStore store,
        CancellationToken cancellationToken,
        Action? onWaiting = null)
    {
        using var lease = await Claim(store, "Reclaiming space from", onWaiting, cancellationToken);

        return store.Reclaim(cancellationToken);
    }

    /// <summary>
    /// Deletes the files sync rescued into the store's quarantine folder because it could not recycle
    /// them.
    /// </summary>
    /// <inheritdoc cref="ReclaimAsync" path="/remarks/para[2]"/>
    public async Task<long> ClearQuarantineAsync(
        ContentStore store,
        CancellationToken cancellationToken,
        Action? onWaiting = null)
    {
        using var lease = await Claim(store, "Emptying the quarantine folder of", onWaiting, cancellationToken);

        return store.ClearQuarantine();
    }

    /// <summary>
    /// Reads every blob in a store, drops the ones that no longer match their address, and says which
    /// mod folders are still running them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half a store cannot answer for itself. <see cref="ContentStore.VerifyAllAsync"/> reports
    /// addresses, because a store deliberately knows nothing about what a file <em>is</em>; the
    /// manifests here turn those into folders and profile names. That matters because <b>removing the
    /// blob is not the repair</b>. Where the entry was hardlinked into a mod folder, that folder is
    /// still holding the same wrong bytes under the same name, and only re-applying its profile
    /// replaces them - so a pass that found something has to be able to say where to go next.
    /// </para>
    /// <para>
    /// Answered from the manifests, so it asks the network nothing and works offline - the same
    /// bargain <see cref="SweepAllAsync"/> makes.
    /// </para>
    /// </remarks>
    /// <inheritdoc cref="ReclaimAsync" path="/remarks/para[2]"/>
    public async Task<StoreVerificationReport> VerifyAsync(
        ContentStore store,
        IProgress<ContentStoreVerificationProgress>? progress,
        CancellationToken cancellationToken,
        Action? onWaiting = null)
    {
        // Exclusive because the pass *repairs*: an address that no longer matches its contents is
        // dropped, and dropping it out from under a sync that is linking it into a mod folder is the
        // one way this could make things worse than it found them.
        using var lease = await Claim(store, "Checking", onWaiting, cancellationToken);

        var result = await store.VerifyAllAsync(progress, cancellationToken);

        return new StoreVerificationReport(result, FindAffectedFolders(store, result.Corrupt));
    }

    /// <summary>
    /// Takes the store to itself, for as long as the housekeeping runs.
    /// </summary>
    /// <param name="verb">
    /// What is being done, so a sync that ends up waiting for this can say what it is waiting for.
    /// </param>
    /// <param name="onWaiting">
    /// Called only where the claim could <em>not</em> be had at once, so a caller can say it is
    /// queueing behind an apply.
    /// </param>
    /// <remarks>
    /// <b>Tried first, then awaited</b>, purely so the waiting message is true when it is shown. A
    /// caller that announced the wait unconditionally would tell somebody watching a verification pass
    /// that it is waiting for an apply throughout the six minutes it is actually reading their disk,
    /// which is worse than saying nothing. The gap between the two calls can cost us the claim, which
    /// only means the message appears and the claim is then granted immediately - harmless, and rare.
    /// </remarks>
    private async Task<IResourceLease> Claim(
        ContentStore store,
        string verb,
        Action? onWaiting,
        CancellationToken cancellationToken)
    {
        var key = ResourceKeys.Store(store);
        var holder = $"{verb} the store on {store.VolumeRoot}";

        if (leases.TryAcquireExclusive(key, holder) is IResourceLease immediate)
        {
            return immediate;
        }

        onWaiting?.Invoke();

        return await leases.AcquireExclusiveAsync(key, holder, cancellationToken);
    }

    /// <summary>
    /// Which mod folders this store serves are running one of the bad addresses.
    /// </summary>
    /// <remarks>
    /// Every served game is asked, not only the drifted ones. A folder whose file still matches
    /// its manifest exactly is the <em>worst</em> case here, not the safe one: it means nothing has
    /// noticed, and under hardlinking that file is the blob that just failed.
    /// </remarks>
    private List<AffectedModFolder> FindAffectedFolders(ContentStore store, IReadOnlyList<string> corrupt)
    {
        if (corrupt.Count == 0)
        {
            return [];
        }

        var bad = new HashSet<string>(corrupt, StringComparer.OrdinalIgnoreCase);
        var affected = new List<AffectedModFolder>();

        foreach (var folder in modFolders.GetAll())
        {
            try
            {
                if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(folder.ModFolder).RootPath, store.RootPath) is false)
                {
                    continue;
                }

                var manifest = manifestStore.TryRead(folder.Target);
                var hits = manifest?.Entries.Count(x => bad.Contains(x.ContentHash)) ?? 0;

                if (hits > 0)
                {
                    affected.Add(new AffectedModFolder(folder.ModFolder, manifest?.ProfileName, hits));
                }
            }
            catch (Exception exception)
            {
                // One folder that cannot be resolved costs its name in the report, not the report.
                logger.LogDebug(exception, "Could not tell whether target {Target} is running a bad blob.", folder.Target);
            }
        }

        return affected;
    }

    /// <summary>
    /// What the mod folders this store serves are currently running, and therefore what a sweep must
    /// leave alone.
    /// </summary>
    /// <remarks>
    /// Dropping one of these would not break anything - the file in the mod folder holds its own
    /// bytes on a copy-served disk and survives losing a name on a hardlinked one - but it would
    /// guarantee a re-download the next time that profile is applied, which is the opposite of what
    /// somebody clicking "sweep" is asking for.
    /// </remarks>
    private IReadOnlySet<string> GetPinnedHashes(ContentStore store)
    {
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in modFolders.GetAll())
        {
            try
            {
                if (FileSystemHelper.ArePathsEqual(storeProvider.GetStoreServing(folder.ModFolder).RootPath, store.RootPath) is false)
                {
                    continue;
                }

                foreach (var entry in manifestStore.TryRead(folder.Target)?.Entries ?? [])
                {
                    pinned.Add(entry.ContentHash);
                }
            }
            catch (Exception exception)
            {
                // A folder that cannot be resolved to a store contributes no pins, which
                // costs a possible re-download rather than a failed sweep.
                logger.LogDebug(exception, "Could not read what target {Target} is running.", folder.Target);
            }
        }

        return pinned;
    }
}
