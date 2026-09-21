using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>
/// A shortcut in the current user's Start Menu that carries the app's identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not decoration: without it Windows does not show the app's notifications at all.</b> An
/// unpackaged desktop app is only known to the notification platform through a Start Menu shortcut
/// naming its executable, and a toast from one that has none is accepted, recorded in its history and
/// then never drawn - no banner, nothing in Action Center, and no error anywhere. Registering the app
/// identity in the registry, which is what the toolkit's own registration does, is not enough on its own.
/// </para>
/// <para>
/// <b>The shell interfaces are declared here</b> because .NET has no managed way to set the one property
/// that matters, <c>System.AppUserModel.ID</c>, and writing it is what ties the shortcut to the identity
/// the process claims. Nothing else in the app talks to the shell this way.
/// </para>
/// </remarks>
public static class StartMenuShortcut
{
    /// <summary>
    /// Makes sure the shortcut exists and names <paramref name="executablePath"/>, rewriting it if it
    /// names anywhere else.
    /// </summary>
    /// <remarks>
    /// Checked on every start rather than once, because the executable moves - a rebuilt debug copy, or
    /// an installed one that updated into a new folder - and a shortcut left pointing at the old place
    /// is one Windows quietly stops attributing notifications through.
    /// </remarks>
    public static void Ensure(string displayName, string executablePath, string appUserModelId)
    {
        var path = PathFor(displayName);

        if (File.Exists(path) && TargetOf(path) is string target
            && string.Equals(target, executablePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(executablePath);
        link.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);

        var store = (IPropertyStore)link;
        var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        var value = PropVariant.FromString(appUserModelId);

        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            value.Clear();
        }

        ((IPersistFile)link).Save(path, remember: true);
    }

    /// <summary>Removes the shortcut, for an uninstall. Absent is not an error.</summary>
    public static void Remove(string displayName)
    {
        var path = PathFor(displayName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }


    private static string PathFor(string displayName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        $"{displayName}.lnk");

    private static string? TargetOf(string path)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(path, 0);

            var target = new StringBuilder(520);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            return target.Length > 0 ? target.ToString() : null;
        }
        catch (Exception exception) when (exception is COMException or IOException)
        {
            // A shortcut that cannot be read is one that gets rewritten.
            return null;
        }
    }


    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxChars);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxChars);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    /// <summary>The one shape of PROPVARIANT this needs: a string, which Windows frees with the store.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        private const ushort _typeString = 31;

        [FieldOffset(0)] private ushort _type;
        [FieldOffset(8)] private IntPtr _pointer;

        public static PropVariant FromString(string value) => new()
        {
            _type = _typeString,
            _pointer = Marshal.StringToCoTaskMemUni(value)
        };

        public void Clear()
        {
            if (_pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_pointer);
                _pointer = IntPtr.Zero;
            }
        }
    }
}
