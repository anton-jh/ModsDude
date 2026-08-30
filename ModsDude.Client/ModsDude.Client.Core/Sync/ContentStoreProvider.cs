using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Turns the machine-wide store settings into the stores themselves.
/// </summary>
public interface IContentStoreProvider
{
    /// <summary>The store that serves mod folders on the disk <paramref name="path"/> is on.</summary>
    ContentStore GetStoreServing(string path);

    /// <summary>
    /// Every store configured on this machine, the serving one included. Install looks across all of
    /// them before the network, and uninstall asks all of them whether a copy already exists
    /// somewhere before keeping one.
    /// </summary>
    IReadOnlyList<ContentStore> GetAllStores();
}


/// <inheritdoc cref="IContentStoreProvider"/>
public sealed class ContentStoreProvider(ClientSettingsRepository settingsRepository)
    : IContentStoreProvider
{
    public ContentStore GetStoreServing(string path)
    {
        var settings = settingsRepository.Settings;
        var servingVolume = settings.GetServingVolume(FileSystemHelper.NormalizeVolumeRoot(path));

        return Build(servingVolume, settings);
    }

    public IReadOnlyList<ContentStore> GetAllStores()
    {
        var settings = settingsRepository.Settings;

        // Both halves matter: a volume can have a store configured that nothing currently points at,
        // and an assignment can name a volume whose store has never been saved.
        var volumes = settings.Stores.Keys
            .Concat(settings.StoreAssignments.Values)
            .Select(FileSystemHelper.NormalizeVolumeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. volumes.Select(x => Build(x, settings))];
    }


    /// <summary>
    /// An unconfigured volume gets the same defaults the settings page would offer it, rather than
    /// refusing to sync until somebody has visited a page to accept them.
    /// </summary>
    private static ContentStore Build(string volumeRoot, ClientSettings settings)
    {
        var configured = settings.Stores.GetValueOrDefault(volumeRoot);

        return new ContentStore(
            volumeRoot,
            configured?.Path ?? ContentStoreSettings.GetDefaultPath(volumeRoot),
            configured?.MaxSizeBytes ?? ContentStoreSettings.DefaultMaxSizeBytes);
    }
}
