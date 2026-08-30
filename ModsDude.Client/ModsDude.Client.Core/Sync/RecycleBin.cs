using System.Runtime.InteropServices;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Sends a file to the Windows Recycle Bin.
/// </summary>
/// <remarks>
/// The destination for anything sync uninstalls that the repo does not know about: a file the user
/// put there, that nothing else has a copy of, and that must never be deleted. The Recycle Bin means
/// recovery uses a mechanism the user already understands, with no ModsDude-specific quarantine to
/// manage, garbage-collect or explain.
/// See docs/07-mod-sync-design.md#uninstall-rules.
/// </remarks>
public interface IRecycleBin
{
    /// <summary>
    /// Whether the volume holding <paramref name="path"/> has a Recycle Bin at all. A drive with it
    /// turned off, and a network path, do not - and there the caller quarantines instead.
    /// </summary>
    bool IsAvailableFor(string path);

    /// <returns>
    /// False when the file is still where it was. The caller then moves it into the store's
    /// quarantine folder, which is the fallback that keeps the never-delete rule true.
    /// </returns>
    bool TryRecycle(string path);
}


/// <inheritdoc cref="IRecycleBin"/>
public sealed partial class ShellRecycleBin : IRecycleBin
{
    private const uint _deleteOperation = 0x0003;

    private const ushort _allowUndo = 0x0040;
    private const ushort _noConfirmation = 0x0010;
    private const ushort _silent = 0x0004;
    private const ushort _noErrorUi = 0x0400;

    /// <summary>
    /// Partially overrides <c>FOF_NOCONFIRMATION</c>: where the shell would permanently destroy the
    /// file instead of recycling it - most often because it is larger than the bin's quota - it asks
    /// first. Declining aborts the operation, which this reports as a failure so the file is
    /// quarantined instead. Without this flag a large mod archive would be silently and
    /// unrecoverably deleted, which is the exact outcome the uninstall rules exist to prevent.
    /// </summary>
    private const ushort _wantNukeWarning = 0x4000;


    public bool IsAvailableFor(string path)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return false;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            var info = new ShQueryRecycleBinInfo
            {
                StructureSize = (uint)Marshal.SizeOf<ShQueryRecycleBinInfo>()
            };

            return SHQueryRecycleBinW(root, ref info) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryRecycle(string path)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return false;
        }

        var from = IntPtr.Zero;

        try
        {
            // The shell wants a double-null-terminated list, so the buffer is built by hand rather
            // than left to string marshalling, which would terminate it once.
            from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + '\0' + '\0');

            var operation = new ShFileOperation
            {
                Function = _deleteOperation,
                From = from,
                Flags = _allowUndo | _noConfirmation | _silent | _noErrorUi | _wantNukeWarning
            };

            var result = SHFileOperationW(ref operation);

            // Aborted covers the user declining a permanent delete, which is a refusal to lose the
            // file rather than an error - and is handled the same way, by quarantining it.
            return result == 0 && operation.AnyOperationsAborted == 0 && File.Exists(path) is false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (from != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(from);
            }
        }
    }


    [LibraryImport("shell32.dll", SetLastError = false)]
    private static partial int SHFileOperationW(ref ShFileOperation operation);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string rootPath, ref ShQueryRecycleBinInfo info);


    /// <summary><c>SHFILEOPSTRUCTW</c>. Blittable throughout so it needs no custom marshalling.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    /// <summary><c>SHQUERYRBINFO</c>. Only whether the call succeeds is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRecycleBinInfo
    {
        public uint StructureSize;
        public long Size;
        public long ItemCount;
    }
}
