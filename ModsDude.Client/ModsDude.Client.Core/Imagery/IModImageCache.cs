namespace ModsDude.Client.Core.Imagery;

public interface IModImageCache
{
    Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken);

    Task WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the least recently used entries until the cache is back under its configured size.
    /// Called on its own only by tests and by a caller that knows it has just written a lot.
    /// </summary>
    Task EvictAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How much the cache is holding, for a settings page to report.
    /// </summary>
    /// <remarks>
    /// Walks the directory, so it belongs off the drawing thread. Reported as-is rather than
    /// remembered: the folder is the only record of what is in there, and a cached number would be
    /// wrong the moment a sweep ran.
    /// </remarks>
    ModImageCacheUsage Measure();

    /// <summary>
    /// Empties the cache.
    /// </summary>
    /// <remarks>
    /// Costs nothing but re-fetching: every entry is either a server derivative addressed by its own
    /// hash or a rendition decoded out of a local archive, so both come back on demand. Files
    /// something is reading are skipped and swept later.
    /// </remarks>
    /// <returns>The bytes reclaimed.</returns>
    long Clear();
}
