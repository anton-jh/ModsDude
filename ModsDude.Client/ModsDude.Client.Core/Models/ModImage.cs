namespace ModsDude.Client.Core.Models;

/// <param name="CacheKey">
/// Stable identity of the image bytes, safe to use as a cache key. Changes whenever the
/// underlying file changes.
/// </param>
/// <param name="Load">
/// Reads the raw, still encoded image bytes. Deliberately lazy - a mod folder can hold well over
/// a thousand mods, and reading every image up front would mean unpacking every archive.
/// </param>
/// <remarks>
/// Says nothing about where the bytes come from, which is the point: a server-backed image is the
/// same record with an HTTP fetch in <paramref name="Load"/>, and the provider, the lazy-loading
/// behaviour and both caches keep working untouched.
/// </remarks>
public record ModImage(string Name, string CacheKey, Func<CancellationToken, Task<byte[]>> Load)
{
    /// <summary>
    /// The bytes are already at the size they are meant to be drawn at, so nothing downstream
    /// re-derives them or keeps a second copy of what it was handed. True of a server derivative,
    /// which is stored pre-sized precisely so that no client has to decode a 512 px DDS to draw
    /// 64 px of it.
    /// </summary>
    public bool IsPreSized { get; init; }

    /// <summary>
    /// The same picture at full resolution, where <see cref="Load"/> reads a smaller rendition.
    /// Null when there is nothing larger to show - a local image is loaded whole and downscaled on
    /// the way to the screen, and an icon has no larger rendition stored at all.
    /// </summary>
    public ModImage? FullSize { get; init; }
}
