using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ModsDude.Client.Core.Disks;

/// <remarks>
/// <c>GetDiskFreeSpaceEx</c> rather than <see cref="DriveInfo"/>: it answers for a network share as
/// well as a drive letter, and it counts the user's disk quota.
/// </remarks>
public sealed partial class DiskSpace(ILogger<DiskSpace> logger) : IDiskSpace
{
    public long? GetAvailableBytes(string volumeRoot)
    {
        // A share root only names the share with its trailing separator, which Path.GetPathRoot leaves off.
        var directory = Path.EndsInDirectorySeparator(volumeRoot)
            ? volumeRoot
            : volumeRoot + Path.DirectorySeparatorChar;

        if (GetDiskFreeSpaceExW(directory, out var available, out _, out _))
        {
            return (long)Math.Min(available, long.MaxValue);
        }

        logger.LogWarning(new Win32Exception(Marshal.GetLastPInvokeError()), "Could not read the free space on {Volume}.", volumeRoot);

        return null;
    }


    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceExW(
        string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);
}
