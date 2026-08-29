using System.Security.Cryptography;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// The SHA-256 that addresses a derivative, as lowercase hex - the same form the server validates
/// and the same form the blob path is built from.
/// </summary>
public static class ModImageHashing
{
    public const int HashLength = 64;


    public static string Compute(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);

        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Whether <paramref name="bytes"/> is what <paramref name="hash"/> addresses. The image blobs
    /// are one globally shared address space that every client caches permanently by hash, which is
    /// only safe while every ingest is checked: without this, one member uploading hostile bytes at
    /// an address another repo references poisons that image on every machine that ever draws it.
    /// </summary>
    public static bool Verify(string hash, ReadOnlySpan<byte> bytes)
    {
        return string.Equals(hash, Compute(bytes), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidHash(string? hash)
    {
        if (hash is null || hash.Length != HashLength)
        {
            return false;
        }

        foreach (var character in hash)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
