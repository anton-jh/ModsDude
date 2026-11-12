using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Models;

internal static class TestOccurrences
{
    /// <summary>A copy found in <paramref name="source"/>, scanned as the mod and version given.</summary>
    public static ModOccurrence Create(
        ModSource source,
        string path,
        long length,
        Func<Stream> open,
        string modId = "FS25_Plough",
        string version = "1.0",
        string? name = null,
        string description = "A mod.")
    {
        return new ModOccurrence(source, new LocalMod(ModKey.From(modId), ModVersionKey.From(version), name ?? modId, description, open)
        {
            FilePath = path,
            FileLength = length
        });
    }
}
