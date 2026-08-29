using ModsDude.Client.Core.Helpers;

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
    public required string Path { get; set; }
    public required long MaxSizeBytes { get; set; }
}
