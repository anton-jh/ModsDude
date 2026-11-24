namespace ModsDude.Server.Domain.Storage;

public enum StorageContainer
{
    Mods,
    Savegames,
    Images
}


public static class StorageContainerNames
{
    public static IReadOnlyList<StorageContainer> All { get; } = [StorageContainer.Mods, StorageContainer.Savegames, StorageContainer.Images];

    /// <summary>The container's name in the storage account.</summary>
    public static string Of(StorageContainer container) => container switch
    {
        StorageContainer.Mods => "mods",
        StorageContainer.Savegames => "savegames",
        StorageContainer.Images => "mod-images",
        _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
    };
}
