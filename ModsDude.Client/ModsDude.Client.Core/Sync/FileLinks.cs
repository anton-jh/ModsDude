using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// The filesystem facts the content store depends on and .NET does not expose: creating a hardlink,
/// how many names a file already has, and which file on disk a name actually refers to.
/// </summary>
/// <remarks>
/// <para>
/// The link count is what makes store accounting honest. An entry hardlinked into a live mod folder
/// costs no additional bytes, so evicting it reclaims nothing, and <see cref="FileInfo"/> cannot
/// answer that - only <c>GetFileInformationByHandle</c> can.
/// See docs/07-mod-sync-design.md#store-eviction-and-the-size-limit.
/// </para>
/// <para>
/// <see cref="TryGetFileIdentity"/> is what tells an in-place rewrite from a rename-over. Both leave
/// a mod folder holding different bytes than the last sync installed; only the first wrote
/// <em>through</em> the hardlink into the shared store blob. The difference is visible in one fact
/// and no others - whether the file in the mod folder is still the same file as the blob - and a
/// link count cannot answer it, because a rename-over leaves the blob with a perfectly ordinary
/// count of one. See docs/07-mod-sync-design.md#detecting-a-rewritten-blob.
/// </para>
/// <para>
/// All three calls are Windows-only. Everywhere else - and on any filesystem that refuses a link,
/// which is what exFAT and network paths do - the caller falls back to copying, which is correct
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
        return TryGetInformation(path) is ByHandleFileInformation information
            ? (int)information.NumberOfLinks
            : null;
    }

    /// <summary>
    /// Which file on which volume this name refers to. Two names of one hardlinked file give equal
    /// identities; two separate files holding identical bytes do not.
    /// </summary>
    /// <returns>
    /// Null where it cannot be established - a platform without the call, a filesystem that reports
    /// no stable index, a file that will not open. Callers treat that as "cannot tell" and do
    /// nothing, which is the safe end of this particular question: the act it gates is deleting a
    /// store blob.
    /// </returns>
    public static FileIdentity? TryGetFileIdentity(string path)
    {
        if (TryGetInformation(path) is not ByHandleFileInformation information)
        {
            return null;
        }

        return new FileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }


    private static ByHandleFileInformation? TryGetInformation(string path)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return null;
        }

        try
        {
            // Shared for read, write and delete: the file may be open in the game at this moment,
            // and asking what it is must not interfere with that.
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return GetFileInformationByHandle(handle, out var information)
                ? information
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
    /// <c>BY_HANDLE_FILE_INFORMATION</c>. Only the link count, the volume serial and the file index
    /// are read; the rest is here because the struct has to match the one the API writes into.
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


/// <summary>
/// Which file, on which volume. The pair NTFS uses to mean one file record, and therefore the thing
/// every name of a hardlinked file has in common.
/// </summary>
/// <remarks>
/// Only ever compared against another identity read at about the same moment. A file index is stable
/// while the file exists and explicitly not stable across deletion and recreation, so this is not
/// something to persist and compare against later.
/// </remarks>
public readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);
