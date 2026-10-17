using System.Runtime.InteropServices;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// The template Explorer names a copy by, such as <c>%s - Copy</c>, in the language Windows runs in.
/// </summary>
/// <remarks>
/// Windows has no API for it. Explorer reads string 4178 of <c>shell32.dll</c>, so this does too, and
/// <see cref="SuffixOf"/> accepts only what has the expected shape.
/// </remarks>
internal static partial class ExplorerCopyName
{
    private const uint _copyTemplateId = 4178;
    private const uint _loadAsDataFile = 0x2;
    private const uint _loadAsImageResource = 0x20;
    private const string _placeholder = "%s";


    /// <summary>The template, or null where Windows does not have it.</summary>
    public static string? ReadTemplate()
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return null;
        }

        var module = LoadLibraryExW("shell32.dll", 0, _loadAsDataFile | _loadAsImageResource);

        if (module == 0)
        {
            return null;
        }

        try
        {
            Span<char> buffer = stackalloc char[256];
            int length;

            unsafe
            {
                fixed (char* pointer = buffer)
                {
                    length = LoadStringW(module, _copyTemplateId, pointer, buffer.Length);
                }
            }

            return length > 0 ? new string(buffer[..length]) : null;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>
    /// What the template appends to a name, or null where it is not a single leading placeholder
    /// followed by a suffix.
    /// </summary>
    public static string? SuffixOf(string? template)
    {
        if (template is null || template.StartsWith(_placeholder, StringComparison.Ordinal) is false)
        {
            return null;
        }

        var suffix = template[_placeholder.Length..];

        return string.IsNullOrWhiteSpace(suffix) || suffix.Contains('%') ? null : suffix;
    }


    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);

    [LibraryImport("user32.dll")]
    private static unsafe partial int LoadStringW(nint instance, uint id, char* buffer, int bufferMax);
}
