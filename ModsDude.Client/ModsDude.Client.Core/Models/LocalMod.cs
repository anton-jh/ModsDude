namespace ModsDude.Client.Core.Models;

/// <param name="GetStream">Opens the mod file itself, for upload.</param>
public record LocalMod(string Id, string Version, string Name, string Description, Func<Stream> GetStream)
{
    /// <summary>
    /// Small square image used in list rows. Null when the mod doesn't ship one.
    /// </summary>
    public LocalModImage? Icon { get; init; }

    /// <summary>
    /// Larger presentation images for the details view. Frequently empty - script-only mods
    /// usually ship nothing but an icon.
    /// </summary>
    public IReadOnlyList<LocalModImage> Images { get; init; } = [];

    public string? Author { get; init; }
}

/// <param name="CacheKey">
/// Stable identity of the image bytes, safe to use as a cache key. Changes whenever the
/// underlying file changes.
/// </param>
/// <param name="Load">
/// Reads the raw, still encoded image bytes. Deliberately lazy - a mod folder can hold well over
/// a thousand mods, and reading every image up front would mean unpacking every archive.
/// </param>
public record LocalModImage(string Name, string CacheKey, Func<CancellationToken, Task<byte[]>> Load);
