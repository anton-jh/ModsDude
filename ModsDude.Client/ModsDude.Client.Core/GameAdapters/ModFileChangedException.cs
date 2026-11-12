namespace ModsDude.Client.Core.GameAdapters;

/// <summary>A mod file is no longer the file that was scanned: its length or last write time changed.</summary>
public sealed class ModFileChangedException(string path)
    : IOException($"'{path}' has changed since it was scanned.")
{
    public string FilePath { get; } = path;
}
