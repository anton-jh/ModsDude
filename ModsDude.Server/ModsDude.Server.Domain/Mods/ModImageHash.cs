using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Domain.Mods;

/// <summary>
/// The SHA-256 that addresses an image derivative, as lowercase hex. Validated rather than taken on
/// trust because the value is a blob path segment in an address space that is global rather than
/// repo-scoped: anything but a hash addresses something that is not an image.
/// </summary>
public static class ModImageHash
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
