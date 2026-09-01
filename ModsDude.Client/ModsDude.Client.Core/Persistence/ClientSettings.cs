using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// Machine-wide client settings - not per repo, not per instance, not per adapter. The content
/// store is addressed by hash and holds no notion of what a file is for, so there is nothing a repo
/// or an adapter would contribute to its scoping.
/// </summary>
public class ClientSettings
{
    /// <summary>Content stores, keyed by the root of the volume each one lives on.</summary>
    public Dictionary<string, ContentStoreSettings> Stores { get; init; } = [];

    /// <summary>
    /// Where decoded and downloaded mod imagery is kept. One per machine rather than one per volume:
    /// a store is per-volume because hardlinks cannot cross volumes, and images are always copies,
    /// so splitting them per volume would only duplicate them. Kept separate from the stores, which
    /// is what keeps the content store from ever being an image source.
    /// </summary>
    public ImageCacheSettings ImageCache { get; init; } = new();

    /// <summary>
    /// Which volume's store serves the mod folders on a volume, keyed by volume root. A volume
    /// served by its own store materialises by hardlink; one served from another disk materialises
    /// by copy, trading sync time for space on the constrained disk. Both are legitimate choices.
    /// </summary>
    public Dictionary<string, string> StoreAssignments { get; init; } = [];

    /// <summary>The store that serves mod folders on <paramref name="volumeRoot"/>, if one is configured.</summary>
    public ContentStoreSettings? GetStoreServing(string volumeRoot)
    {
        return Stores.GetValueOrDefault(GetServingVolume(volumeRoot));
    }

    /// <summary>
    /// The volume whose store serves <paramref name="volumeRoot"/>. Unassigned volumes are served
    /// by their own store, which is the default and the cheaper of the two.
    /// </summary>
    public string GetServingVolume(string volumeRoot)
    {
        var normalized = FileSystemHelper.NormalizeVolumeRoot(volumeRoot);

        return StoreAssignments.TryGetValue(normalized, out var servingVolume)
            ? servingVolume
            : normalized;
    }
}

public class ContentStoreSettings
{
    /// <summary>
    /// What an unconfigured volume gets. A store has to have some ceiling before the first sync
    /// writes to it, and refusing to sync until somebody has visited a page to accept a number would
    /// be a worse answer than starting from the one the settings page offers.
    /// </summary>
    public const long DefaultMaxSizeBytes = 100L * 1024 * 1024 * 1024;


    public required string Path { get; set; }
    public required long MaxSizeBytes { get; set; }


    public static string GetDefaultPath(string volumeRoot)
        => System.IO.Path.Combine(volumeRoot, "ModsDude", "store");
}

public class ImageCacheSettings
{
    /// <summary>
    /// A few hundred megabytes is the realistic ceiling across several repos - at ~6 KB a thumbnail,
    /// every icon in a 3,000-version repo is around 20 MB. Everything in the cache is
    /// re-downloadable or re-derivable, so eviction never has to ask the user anything.
    /// </summary>
    public const long DefaultMaxSizeBytes = 512L * 1024 * 1024;


    public string Path { get; set; } = DefaultPath;
    public long MaxSizeBytes { get; set; } = DefaultMaxSizeBytes;


    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModsDude",
        "image-cache");
}
