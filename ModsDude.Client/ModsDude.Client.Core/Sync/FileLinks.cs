using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// The two filesystem facts the content store depends on and .NET does not expose: creating a
/// hardlink, and how many names a file already has.
/// </summary>
/// <remarks>
/// <para>
/// The link count is what makes store accounting honest. An entry hardlinked into a live mod folder
/// costs no additional bytes, so evicting it reclaims nothing, and <see cref="FileInfo"/> cannot
/// answer that - only <c>GetFileInformationByHandle</c> can.
/// See docs/07-mod-sync-design.md#store-eviction-and-the-size-limit.
/// </para>
/// <para>
/// Both calls are Windows-only. Everywhere else - and on any filesystem that refuses a link, which
/// is what exFAT and network paths do - the caller falls back to copying, which is correct
/// everywhere and only ever costs bytes.
/// </para>
/// </remarks>
public static partial class FileLinks
{
    /// <summary>
    /// Points <paramref name="linkPath"/> at the same file data as <paramref name="existingPath"/>.
    /// </summary>
    /// <returns>
    /// False where the filesystem will not do it - a different volume, exFAT, a network path, or a
    /// platform without hardlinks. Never throws: refusing is an ordinary answer here, and the caller
    /// copies instead.
    /// </returns>
    public static bool TryCreateHardLink(string linkPath, string existingPath)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return false;
        }

        try
        {
            return CreateHardLinkW(linkPath, existingPath, IntPtr.Zero);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>How many directory entries name this file's data.</summary>
    /// <returns>
    /// Null where it cannot be established. Callers treat that as "assume it is shared" rather than
    /// as one, so an unreadable file is never evicted on a guess.
    /// </returns>
    public static int? TryGetLinkCount(string path)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return null;
        }

        try
        {
            // Shared for read, write and delete: the file may be open in the game at this moment,
            // and asking how many names it has must not interfere with that.
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return GetFileInformationByHandle(handle, out var information)
                ? (int)information.NumberOfLinks
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }


    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string linkPath, string existingPath, IntPtr securityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeHandle file, out ByHandleFileInformation information);


    /// <summary>
    /// <c>BY_HANDLE_FILE_INFORMATION</c>. Only the link count is read; the rest is here because the
    /// struct has to match the one the API writes into.
    /// </summary>
    /// <remarks>
    /// Timestamps are pairs of 32-bit halves rather than a 64-bit field, deliberately: a native
    /// <c>FILETIME</c> is two DWORDs and aligns to four, so a <see cref="long"/> would align to
    /// eight and shift every field after it - which would read the link count out of the wrong
    /// bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
