using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// The machine-wide image cache: downloaded derivatives, keyed by their own hash, alongside
/// renditions decoded out of a local archive, keyed by the archive entry and the size they were
/// decoded at.
/// </summary>
/// <remarks>
/// <para>
/// A downloaded derivative arrives pre-sized, so its key carries no size and can never invalidate -
/// each one crosses the wire once per machine, ever. A local rendition is keyed by the file it came
/// from, so it falls out of use when the file changes and is evicted like anything else.
/// </para>
/// <para>
/// Least-recently-used is approximated by last-write time. Windows does not maintain last-access
/// time by default, and a cache this hot cannot afford a metadata write per read, so a hit only
/// refreshes the timestamp once it has gone stale.
/// </para>
/// </remarks>
public sealed class ModImageCache(Func<ImageCacheSettings> getSettings, ILogger<ModImageCache> logger)
{
    /// <summary>
    /// Walking the directory costs the same whether one file or a thousand were added since, so
    /// sweeps are spaced by how much has been written rather than run per write.
    /// </summary>
    private const long _bytesBetweenSweeps = 8L * 1024 * 1024;

    /// <summary>Evicting to just under the cap would sweep again on the next write.</summary>
    private const double _targetFillAfterEviction = 0.9;

    private static readonly TimeSpan _timestampRefreshInterval = TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _sweepLock = new(1, 1);

    private long _writtenSinceSweep;


    public async Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = GetPath(key);

        try
        {
            if (File.Exists(path) is false)
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

            Touch(path);

            return bytes;
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // A half-written or locked entry costs a re-fetch, which is the whole contract of a
            // cache. Not worth interrupting anybody for - but a cache that misses every time is a
            // slow app with no other symptom, so it does not get to be silent.
            logger.LogDebug(exception, "Could not read the cached image {Key}.", key);

            return null;
        }
    }

    public async Task WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = GetPath(key);

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            var temporaryPath = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";

            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            logger.LogDebug(exception, "Could not cache the image {Key}.", key);

            return;
        }

        if (Interlocked.Add(ref _writtenSinceSweep, bytes.Length) >= _bytesBetweenSweeps)
        {
            await EvictAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Drops the least recently used entries until the cache is back under its configured size.
    /// Called on its own only by tests and by a caller that knows it has just written a lot.
    /// </summary>
    public async Task EvictAsync(CancellationToken cancellationToken)
    {
        if (await _sweepLock.WaitAsync(0, cancellationToken) is false)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref _writtenSinceSweep, 0);

            var settings = getSettings();

            if (Directory.Exists(settings.Path) is false)
            {
                return;
            }

            var entries = new DirectoryInfo(settings.Path)
                .EnumerateFiles("*.img", SearchOption.TopDirectoryOnly)
                .Select(x => (File: x, x.Length, x.LastWriteTimeUtc))
                .ToList();

            var total = entries.Sum(x => x.Length);

            if (total <= settings.MaxSizeBytes)
            {
                return;
            }

            var target = (long)(settings.MaxSizeBytes * _targetFillAfterEviction);

            foreach (var entry in entries.OrderBy(x => x.LastWriteTimeUtc))
            {
                if (total <= target)
                {
                    return;
                }

                try
                {
                    entry.File.Delete();
                    total -= entry.Length;
                }
                catch (Exception exception)
                {
                    // Something else is reading it. It will be swept next time.
                    logger.LogDebug(exception, "Could not evict the cached image {File}.", entry.File.FullName);
                }
            }
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // An unreachable or unwritable cache folder means a slower app, not a broken one.
            logger.LogWarning(exception, "Sweeping the image cache failed.");
        }
        finally
        {
            _sweepLock.Release();
        }
    }


    /// <summary>
    /// How much the cache is holding, for a settings page to report.
    /// </summary>
    /// <remarks>
    /// Walks the directory, so it belongs off the drawing thread. Reported as-is rather than
    /// remembered: the folder is the only record of what is in there, and a cached number would be
    /// wrong the moment a sweep ran.
    /// </remarks>
    public ModImageCacheUsage Measure()
    {
        try
        {
            var path = getSettings().Path;

            if (Directory.Exists(path) is false)
            {
                return ModImageCacheUsage.Empty;
            }

            var entries = new DirectoryInfo(path).EnumerateFiles("*.img", SearchOption.TopDirectoryOnly).ToList();

            return new ModImageCacheUsage(entries.Count, entries.Sum(x => x.Length));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not measure the image cache.");

            return ModImageCacheUsage.Empty;
        }
    }

    /// <summary>
    /// Empties the cache.
    /// </summary>
    /// <remarks>
    /// Costs nothing but re-fetching: every entry is either a server derivative addressed by its own
    /// hash or a rendition decoded out of a local archive, so both come back on demand. Files
    /// something is reading are skipped and swept later.
    /// </remarks>
    /// <returns>The bytes reclaimed.</returns>
    public long Clear()
    {
        long reclaimed = 0;

        try
        {
            var path = getSettings().Path;

            if (Directory.Exists(path) is false)
            {
                return 0;
            }

            foreach (var entry in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var length = entry.Length;

                    entry.Delete();
                    reclaimed += length;
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Could not delete the cached image {File}.", entry.FullName);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clear the image cache.");
        }

        return reclaimed;
    }


    /// <summary>
    /// Flat, and named by a digest of the key rather than by the key: a key holds a file path, and
    /// a downloaded derivative's key is already a hash, so neither is something to build a path out
    /// of directly.
    /// </summary>
    private string GetPath(string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return System.IO.Path.Combine(getSettings().Path, $"{Convert.ToHexStringLower(digest, 0, 16)}.img");
    }

    private void Touch(string path)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(path);

            if (DateTime.UtcNow - written > _timestampRefreshInterval)
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
        }
        catch (Exception exception)
        {
            // Losing a touch costs an entry its place in the eviction order, nothing more.
            logger.LogDebug(exception, "Could not touch the cached image {File}.", path);
        }
    }
}

/// <summary>What the image cache is holding, for a settings page to report.</summary>
public sealed record ModImageCacheUsage(int Entries, long TotalBytes)
{
    public static ModImageCacheUsage Empty { get; } = new(0, 0);
}
