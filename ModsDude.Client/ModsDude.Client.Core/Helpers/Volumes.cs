using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// Which volume a path really lives on, through junctions, symbolic links, subst drives and folder
/// mount points. Windows only; elsewhere every answer is null and callers fall back to the path root.
/// </summary>
internal static partial class Volumes
{
    private const uint _openExisting = 3;
    private const uint _backupSemantics = 0x02000000;
    private const uint _shareAll = 0x1 | 0x2 | 0x4;
    private const int _maxPath = 32 * 1024;


    /// <summary>The mount point of the volume holding <paramref name="fullPath"/>, ending in a separator.</summary>
    public static string? TryGetVolumeRoot(string fullPath)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return null;
        }

        try
        {
            var resolved = TryResolveFinalPath(NearestExisting(fullPath)) ?? fullPath;
            var buffer = new char[_maxPath];

            return GetVolumePathNameW(resolved, buffer, (uint)buffer.Length)
                ? Path.TrimEndingDirectorySeparator(new string(buffer, 0, Array.IndexOf(buffer, '\0'))) + Path.DirectorySeparatorChar
                : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The volume's <c>\\?\Volume{guid}\</c> name, for a mount point from <see cref="TryGetVolumeRoot"/>.</summary>
    public static string? TryGetVolumeName(string volumeRoot)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return null;
        }

        try
        {
            var buffer = new char[64];

            return GetVolumeNameForVolumeMountPointW(volumeRoot, buffer, (uint)buffer.Length)
                ? new string(buffer, 0, Array.IndexOf(buffer, '\0'))
                : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }


    /// <summary>A path that does not exist yet lives where its nearest existing parent does.</summary>
    private static string NearestExisting(string fullPath)
    {
        var current = fullPath;

        while (Path.Exists(current) is false && Path.GetDirectoryName(current) is string parent)
        {
            current = parent;
        }

        return current;
    }

    private static string? TryResolveFinalPath(string path)
    {
        using SafeFileHandle handle = CreateFileW(path, 0, _shareAll, IntPtr.Zero, _openExisting, _backupSemantics, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[_maxPath];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);

        if (length == 0 || length >= buffer.Length)
        {
            return null;
        }

        var final = new string(buffer, 0, (int)length);

        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + final[8..];
        }

        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }


    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathNameW(string fileName, [Out] char[] volumePath, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, [Out] char[] volumeName, uint length);
}
