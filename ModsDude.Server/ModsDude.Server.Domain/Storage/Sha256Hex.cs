using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// A SHA-256 as lowercase hex: the one format the system records content hashes in. Validated rather
/// than taken on trust wherever it arrives, because it addresses blobs (images and savegames) and is
/// compared byte for byte against what clients compute.
/// </summary>
public static class Sha256Hex
{
    public const int Length = 64;


    public static bool IsValid(string? hash)
    {
        if (hash is null || hash.Length != Length)
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

    public static string Validated(string hash)
    {
        return IsValid(hash)
            ? hash
            : throw new DomainValidationException($"'{hash}' is not a lowercase hex SHA-256");
    }
}
