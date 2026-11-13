using ModsDude.Client.Core.Import;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What a savegame's contents hash to: the files it holds, by name and by their own SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over the contents, never over the archive's bytes.</b> How a zip compresses depends on the
/// runtime's zlib, so two clients packing the same slot can produce different archives - and a slot
/// that hashed differently after an update would read as played everywhere at once.
/// </para>
/// <para>
/// Each entry is its archive name, a NUL, its hash and a newline, in ordinal order of name. File
/// names cannot hold a NUL or a newline, so no two different sets of files share a manifest.
/// </para>
/// </remarks>
public static class SavegameContentHash
{
    /// <param name="entries">Archive name (forward slashes) and SHA-256 of each file, in any order.</param>
    public static string Of(IEnumerable<(string Path, string Hash)> entries)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (path, hash) in entries.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            digest.AppendData(Encoding.UTF8.GetBytes(path));
            digest.AppendData([0]);
            digest.AppendData(Encoding.UTF8.GetBytes(hash));
            digest.AppendData([(byte)'\n']);
        }

        return ModContentHasher.Format(digest.GetCurrentHash());
    }
}
