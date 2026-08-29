using System.Runtime.InteropServices;

namespace ModsDude.Client.Core.Helpers;

public static class KnownFolders
{
    private static readonly Guid _downloads = new("374DE290-123F-4565-9164-39C4925E467B");


    /// <summary>
    /// The system Downloads folder, or null where it cannot be located.
    /// </summary>
    /// <remarks>
    /// .NET has no <c>SpecialFolder</c> for Downloads, and relocating it is common enough that
    /// '%USERPROFILE%\Downloads' is a fallback rather than the first try - on a machine where it has
    /// been moved, that path simply does not exist and the user's mods are not in it.
    /// </remarks>
    public static string? GetDownloads()
    {
        if (OperatingSystem.IsWindows() && TryGetKnownFolderPath(_downloads) is string known)
        {
            return known;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(profile))
        {
            return null;
        }

        var fallback = Path.Combine(profile, "Downloads");

        return Directory.Exists(fallback) ? fallback : null;
    }


    private static string? TryGetKnownFolderPath(Guid folderId)
    {
        var result = IntPtr.Zero;

        try
        {
            return SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out result) == 0
                ? Marshal.PtrToStringUni(result)
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (result != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(result);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);
}
