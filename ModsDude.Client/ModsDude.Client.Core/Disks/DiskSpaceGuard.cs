namespace ModsDude.Client.Core.Disks;

public sealed class DiskSpaceGuard(IDiskSpace diskSpace) : IDiskSpaceGuard
{
    /// <summary>Kept free on top of what the work needs, so the game and Windows still have room to write.</summary>
    public const long ReserveBytes = 1024L * 1024 * 1024;


    public void EnsureRoomFor(DiskSpaceNeeds needs)
    {
        var shortfalls = new List<DiskShortfall>();

        foreach (var need in needs.ByVolume)
        {
            // Unreadable, which DiskSpace has logged. The work goes ahead, and a disk that really is
            // full fails it as it writes, before anything in a game's folders is touched.
            if (diskSpace.GetAvailableBytes(need.VolumeRoot) is not long available)
            {
                continue;
            }

            var missing = need.Bytes + ReserveBytes - available;

            if (missing > 0)
            {
                shortfalls.Add(new DiskShortfall(need.VolumeRoot, missing));
            }
        }

        if (shortfalls.Count > 0)
        {
            throw new NotEnoughDiskSpaceException(shortfalls);
        }
    }
}
