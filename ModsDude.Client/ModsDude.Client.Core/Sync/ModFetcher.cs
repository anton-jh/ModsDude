using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Sync;

/// <summary>One file a store has to hold: which version of which mod it is, and what it must contain.</summary>
internal sealed record ModFetch(ModKey ModId, ModVersionKey VersionId, string Hash, string DisplayName);


/// <summary>
/// Fills a store: another disk's store first, the network second. A disk-to-disk copy beats a
/// download every time and leaves the blob local for the next install to this disk.
/// </summary>
/// <remarks>
/// <b>Several at once</b>, because a download's cost is mostly per file - a link to mint, a first
/// byte to wait for, one connection's ceiling - and most mods are small. Copies from another store
/// still go one at a time: several at once on one spinning disk is slower than one. Additive only,
/// so it never touches a mod folder; the caller holds the stores' shared lease.
/// </remarks>
internal sealed class ModFetcher(IFilesClient filesClient, IModFileDownloader downloader, ILogger logger)
{
    /// <summary>
    /// Mods fetched at once. Their range requests share one connection budget in the downloader,
    /// so this buys overlap for small files rather than more connections for large ones.
    /// </summary>
    private const int _concurrentFetches = 4;


    /// <returns>What could not be fetched. Empty where the serving store now holds every one.</returns>
    public async Task<IReadOnlyList<ModSyncFailure>> FetchAsync(
        Guid repoId,
        ContentStore servingStore,
        IReadOnlyList<ContentStore> allStores,
        IEnumerable<ModFetch> fetches,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Asked of the store now rather than when the caller planned: another apply may have fetched
        // some of these since.
        var wanted = fetches
            .Where(x => servingStore.Contains(x.Hash) is false)
            .DistinctBy(x => x.Hash, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var failures = new List<ModSyncFailure>();
        var run = new FetchRun(wanted.Count, progress);
        using var copying = new SemaphoreSlim(1);

        await Parallel.ForEachAsync(
            wanted,
            new ParallelOptions { MaxDegreeOfParallelism = _concurrentFetches, CancellationToken = cancellationToken },
            async (fetch, ct) =>
            {
                run.Report(fetch, 0, 0);

                try
                {
                    await FetchOneAsync(repoId, servingStore, allStores, fetch, run, copying, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Fetching {Mod} {Version} failed during sync.", fetch.ModId.Value, fetch.VersionId.Value);

                    lock (failures)
                    {
                        failures.Add(new ModSyncFailure(fetch.DisplayName, exception.Message)
                        {
                            Exception = exception,
                            MissingOnServer = exception is ApiException<CustomProblemDetails> { Result.Type: ProblemType.FileNotFound }
                        });
                    }
                }

                run.Finish(fetch);
            });

        progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, wanted.Count, wanted.Count));

        return failures;
    }

    private async Task FetchOneAsync(
        Guid repoId,
        ContentStore servingStore,
        IReadOnlyList<ContentStore> allStores,
        ModFetch fetch,
        FetchRun run,
        SemaphoreSlim copying,
        CancellationToken cancellationToken)
    {
        var elsewhere = allStores.FirstOrDefault(x =>
            FileSystemHelper.ArePathsEqual(x.RootPath, servingStore.RootPath) is false &&
            x.Contains(fetch.Hash));

        // Known up front for a cross-store copy, and only once the response headers arrive for a
        // download - so the row can show a proportion in both cases rather than only one.
        var totalBytes = elsewhere?.GetSize(fetch.Hash) ?? 0;

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
                    run.Report(fetch, x, totalBytes);
                }
            }
        });

        if (elsewhere is not null)
        {
            await copying.WaitAsync(cancellationToken);

            try
            {
                await servingStore.CopyFromAsync(elsewhere, fetch.Hash, report, cancellationToken);
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
                RepoId = repoId,
                ModId = fetch.ModId.Value,
                VersionId = fetch.VersionId.Value
            },
            cancellationToken);

        using var download = await downloader.OpenAsync(link.Link, report, cancellationToken);

        totalBytes = download.Length ?? 0;

        // Verified against what the repo declared before it is stored, never after. This is the
        // check that makes a store shared between repos safe; see ContentStore.IngestAsync.
        await servingStore.IngestAsync(download.Content, fetch.Hash, report, cancellationToken);
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


        public void Report(ModFetch fetch, long bytesTransferred, long totalBytes)
        {
            lock (_gate)
            {
                progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, _completed, total)
                {
                    ModId = fetch.ModId.Value,
                    Detail = fetch.DisplayName,
                    BytesTransferred = bytesTransferred,
                    TotalBytes = totalBytes,
                    Concurrent = true
                });
            }
        }

        public void Finish(ModFetch fetch)
        {
            lock (_gate)
            {
                _completed++;

                progress?.Report(new ModSyncProgress(ModSyncPhase.Fetching, _completed, total)
                {
                    ModId = fetch.ModId.Value,
                    Detail = fetch.DisplayName,
                    Concurrent = true,
                    ItemFinished = true
                });
            }
        }
    }
}
