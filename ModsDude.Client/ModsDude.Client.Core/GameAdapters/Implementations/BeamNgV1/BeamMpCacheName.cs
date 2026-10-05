using System.Text.RegularExpressions;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// How the BeamMP launcher names the files in its cache: the server's file name with the first
/// eight hex digits of its SHA-256 before the extension, <c>truck-1a2b3c4d.zip</c>.
/// </summary>
/// <remarks>
/// The launcher uses a cached file without downloading only when it sits under exactly this name
/// and its SHA-256 matches what the server announced, so a file placed under any other name is a
/// file it never reads.
/// </remarks>
public static partial class BeamMpCacheName
{
    private const int _hashLength = 8;


    /// <param name="serverFileName">What the server calls the file, which the launcher keeps the stem of.</param>
    /// <param name="contentHash">The file's SHA-256, as lowercase hex.</param>
    public static string For(string serverFileName, string contentHash)
    {
        if (contentHash.Length < _hashLength)
        {
            throw new ArgumentException($"'{contentHash}' is too short to be a SHA-256.", nameof(contentHash));
        }

        return $"{Path.GetFileNameWithoutExtension(serverFileName)}-{contentHash[.._hashLength]}{Path.GetExtension(serverFileName)}";
    }

    /// <summary>
    /// The name the server gave a cached file, with one hash suffix taken off. A file without one -
    /// cached by an older launcher, from a server that announced no hashes - keeps its name.
    /// </summary>
    public static string ServerFileName(string cachedFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(cachedFileName);
        var match = HashSuffix().Match(stem);

        return match.Success
            ? stem[..match.Index] + Path.GetExtension(cachedFileName)
            : cachedFileName;
    }


    [GeneratedRegex("-[0-9a-f]{8}$")]
    private static partial Regex HashSuffix();
}
