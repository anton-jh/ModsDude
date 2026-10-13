namespace ModsDude.Client.Core.Disks;

/// <summary>Refuses work that would fill a disk.</summary>
public interface IDiskSpaceGuard
{
    /// <exception cref="NotEnoughDiskSpaceException">A volume would be left with less than the reserve free.</exception>
    void EnsureRoomFor(DiskSpaceNeeds needs);
}
