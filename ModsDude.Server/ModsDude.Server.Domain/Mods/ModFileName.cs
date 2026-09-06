namespace ModsDude.Server.Domain.Mods;

/// <summary>
/// What a mod version's file is called on disk, in the casing of the machine it was imported from.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModId"/> is the identity and arrives normalized to lower case, because it becomes a
/// blob path segment and half of a primary key, both of which are case-sensitive - see
/// docs/09-mod-catalog.md#the-casing-trap. That normalization used to reach the mod folder as well:
/// the client rebuilt the filename out of the id, so applying a profile renamed every archive to
/// lower case. Carrying the spelling <em>alongside</em> the identity is what lets the id stay
/// normalized while the file keeps its name.
/// </para>
/// <para>
/// One member of a repo chooses this value and every other member's client writes a file with it, so
/// it is checked rather than trusted. A valid name is a bare file name - no separator, no traversal,
/// nothing a path normalizer would rewrite - and it has to <b>belong to the mod it is registered
/// under</b>: its stem, lower-cased, is the mod id. A member can therefore change how their own
/// mod's file is spelled, and nothing else.
/// </para>
/// <para>
/// A static rule rather than a value struct on purpose: <c>ConfigureValueObjectConversionsFromAssembly</c>
/// maps every value type in this assembly that has a <c>Value</c> property by invoking its
/// single-argument constructor, and a type whose whole point is that it cannot be built unchecked
/// has no such constructor to invoke. The entity stores the validated string, next to
/// <see cref="ModVersion.ContentHash"/>, which is validated the same way and for the same reason.
/// </para>
/// </remarks>
public static class ModFileName
{
    /// <summary>Long enough for any real mod archive, short enough to fit a path component anywhere.</summary>
    private const int _maxLength = 255;

    /// <summary>
    /// Windows' invalid file name characters, hardcoded rather than taken from
    /// <see cref="Path.GetInvalidFileNameChars"/>: the server may well run on Linux, where that set
    /// is a slash and a null, and the value has to be safe on the Windows machine that writes it.
    /// </summary>
    private static readonly char[] _forbidden = @"/\:*?""<>|".ToCharArray();


    /// <summary>Whether <paramref name="raw"/> can be written as the file of mod <paramref name="modId"/>.</summary>
    public static bool IsValidFor(ModId modId, string? raw)
    {
        if (IsWellFormed(raw) is false)
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(raw!);

        if (string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        // The same normalization the client applies when it derives the id from the filename, so the
        // two agree by construction rather than by both remembering to.
        return string.Equals(stem.Trim().ToLowerInvariant(), modId.Value, StringComparison.Ordinal);
    }


    private static bool IsWellFormed(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > _maxLength)
        {
            return false;
        }

        // Windows strips a trailing space or dot when it normalizes a path, so a name carrying one
        // could never be recreated as written - which is the only reason to carry it. This is also
        // what rejects '.' and '..'.
        if (raw != raw.TrimEnd() || raw.EndsWith('.'))
        {
            return false;
        }

        if (raw.IndexOfAny(_forbidden) >= 0 || raw.Any(char.IsControl))
        {
            return false;
        }

        return true;
    }
}
