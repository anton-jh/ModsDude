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
public record ModImage(string Name, string CacheKey, Func<CancellationToken, Task<byte[]>> Load);
