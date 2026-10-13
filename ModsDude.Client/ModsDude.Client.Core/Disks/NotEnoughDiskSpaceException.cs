using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Disks;

public sealed class NotEnoughDiskSpaceException(IReadOnlyList<DiskShortfall> shortfalls)
    : UserFriendlyException(
        $"Not enough disk space. Free up {string.Join(" and ", shortfalls.Select(x => $"{ByteSize.Describe(x.MissingBytes)} on {x.VolumeRoot}"))}.",
        $"Short by {string.Join(", ", shortfalls.Select(x => $"{x.MissingBytes} bytes on {x.VolumeRoot}"))}, a reserve of {DiskSpaceGuard.ReserveBytes} bytes included. Nothing was written.")
{
    public IReadOnlyList<DiskShortfall> Shortfalls { get; } = shortfalls;
}


/// <param name="MissingBytes">What has to be freed on the volume, the reserve included.</param>
public sealed record DiskShortfall(string VolumeRoot, long MissingBytes);
