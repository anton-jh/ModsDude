using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// Machine-wide client settings - not per repo, not per game, not per adapter. The content
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

    /// <summary>How fast mods and savegames may move to and from storage. See <see cref="Transfers.TransferLimits"/>.</summary>
    public TransferLimitSettings Transfers { get; init; } = new();

    /// <summary>How the app behaves while nobody is looking at it. See <see cref="BackgroundSettings"/>.</summary>
    public BackgroundSettings Background { get; init; } = new();

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
        => System.IO.Path.Combine(volumeRoot, AppIdentity.Name, "store");
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
        AppIdentity.Name,
        "image-cache");
}

/// <summary>
/// Speed limits on the traffic to and from storage, one per direction. Null is no limit, which is
/// the default: the app is usually the only thing on the line that matters, and a limit nobody asked
/// for would only make every sync slower than it has to be.
/// </summary>
public class TransferLimitSettings
{
    public long? DownloadBytesPerSecond { get; set; }
    public long? UploadBytesPerSecond { get; set; }
}


/// <summary>
/// What the app does when its window is not on screen.
/// </summary>
public class BackgroundSettings
{
    /// <summary>
    /// Whether closing the window hides it to the tray instead of quitting. On by default: the drift
    /// check is only worth anything to somebody who is not looking at the window, and an app that quits
    /// when its window is closed cannot tell them anything.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Whether the app may put up Windows notifications while its window is not in front: drift, and
    /// the toasts a finished action would otherwise draw to a window nobody is looking at.
    /// </summary>
    public bool Notifications { get; set; } = true;

    /// <summary>
    /// Whether the user has been told, once, that closing the window leaves the app in the tray. Kept
    /// so the explanation is given the first time it is needed and never again.
    /// </summary>
    public bool TrayHintShown { get; set; }
}
