namespace ModsDude.Client.Core.Helpers;
public static class FileSystemHelper
{
    public static string GetAppDataDirectory()
    {
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localAppDataPath, "ModsDude");
    }

    /// <summary>
    /// The volume a path lives on, in the form settings are keyed by. Upper-cased rather than
    /// compared with a case-insensitive comparer, because the key survives a round trip through
    /// json which does not carry the dictionary's comparer.
    /// </summary>
    public static string NormalizeVolumeRoot(string path)
    {
        return (Path.GetPathRoot(Path.GetFullPath(path)) ?? path).ToUpperInvariant();
    }

    /// <summary>
    /// Whether two paths name the same location. Case-insensitive and separator-insensitive, which
    /// is what Windows means by the same folder.
    /// </summary>
    public static bool ArePathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(NormalizePathForComparison(left), NormalizePathForComparison(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A path in the form two references to the same folder always agree on. Lower-cased rather than
    /// left to a case-insensitive comparer, because callers use it to build keys that outlive the
    /// comparer - json does not carry one.
    /// </summary>
    public static string NormalizePathForComparison(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant();
    }
}
