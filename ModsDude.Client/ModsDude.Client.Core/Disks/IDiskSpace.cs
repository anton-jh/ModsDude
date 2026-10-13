namespace ModsDude.Client.Core.Disks;

public interface IDiskSpace
{
    /// <summary>The bytes the current user may still write to the volume, or null where it cannot be read.</summary>
    long? GetAvailableBytes(string volumeRoot);
}
