using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// The file name a store gives one of its rows, built out of strings an adapter author wrote.
/// </summary>
/// <remarks>
/// <para>
/// <b>Encoded here rather than by a rule adapter authors have to obey.</b> Two adapter-authored
/// strings end up in a manifest's name - a game identity's discriminator, which a scripted adapter
/// declares from inside its script, and a target key. A third rule beside the two for the
/// discriminator would turn a bad string into a manifest that cannot be written, found at sync time
/// on somebody else's machine; encoding at the store cannot be violated at all.
/// </para>
/// <para>
/// <b>Legible, and deliberately not reversible.</b> Ordinary parts pass through untouched -
/// <c>_farming_simulator#fs25</c> and <c>mods</c> make <c>_farming_simulator#fs25_mods</c> - and
/// anything else is escaped rather than refused. Nothing ever reads a name back into its parts: a
/// store finds a row by building the name it would have written for it, and sweeps unknown rows by
/// building every name it still expects.
/// </para>
/// <para>
/// Which is what makes the separator affordable. Parts are joined with <c>_</c> and an underscore
/// inside a part passes through, so <c>("a_b", "c")</c> and <c>("a", "b_c")</c> land on one name.
/// Escaping it would cost the legibility this exists for - <c>_farming_simulator</c> is mostly
/// underscores - and the collision needs one adapter to pick a discriminator and a target key that
/// overlap, which is the same class of mistake as renaming a target key and has the same answer:
/// keys are the adapter author's to keep stable.
/// </para>
/// </remarks>
public static class StoreFileName
{
    /// <summary>
    /// How long a name may get before it is truncated and stamped. Windows allows 255 characters in
    /// one path component, and the rest of that budget is the extension plus the <c>.tmp</c> an
    /// atomic write puts on the end.
    /// </summary>
    private const int _maximumLength = 120;

    private const char _escape = '%';
    private const char _separator = '_';

    /// <summary>
    /// Escaped as a whole rather than at the front, because a reserved name is reserved with any
    /// extension on it: <c>CON.json</c> is as unopenable as <c>CON</c>.
    /// </summary>
    private static readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };


    /// <summary>
    /// The name, without an extension, for a row addressed by these parts.
    /// </summary>
    public static string For(params IEnumerable<string> parts)
    {
        var name = string.Join(_separator, parts.Select(x => Encode(x)));

        if (_reservedNames.Contains(name.Split('.')[0]))
        {
            // Escaping the first character is enough to stop the name being a device, and leaves the
            // rest of it readable.
            name = $"{Encode(name[..1], everything: true)}{name[1..]}";
        }

        if (name.EndsWith('.'))
        {
            // Windows strips a trailing dot on the way to the filesystem, so two names differing only
            // by one would be the same file.
            name = $"{name[..^1]}{Encode(".", everything: true)}";
        }

        return name.Length > _maximumLength ? Truncate(name) : name;
    }


    /// <summary>
    /// Percent-encoding over UTF-8, as a URL does it, so an escape is recognisable as one and the
    /// escape character itself has an escape.
    /// </summary>
    private static string Encode(string part, bool everything = false)
    {
        if (everything is false && part.All(IsSafe))
        {
            return part;
        }

        var builder = new StringBuilder(part.Length);

        foreach (var character in part)
        {
            if (everything is false && IsSafe(character))
            {
                builder.Append(character);

                continue;
            }

            foreach (var b in Encoding.UTF8.GetBytes([character]))
            {
                builder.Append(_escape).Append(b.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The characters that survive. Everything a filesystem refuses is outside the set by
    /// construction, rather than being a list of what to exclude that a new filesystem could outgrow.
    /// </summary>
    private static bool IsSafe(char character)
    {
        return character switch
        {
            >= 'a' and <= 'z' => true,
            >= 'A' and <= 'Z' => true,
            >= '0' and <= '9' => true,
            '_' or '-' or '.' or '#' or '@' or '~' or '+' => true,
            _ => false
        };
    }

    /// <summary>
    /// Keeps the readable front of a long name and stamps it with a digest of the whole, so two rows
    /// that agree for their first hundred characters stay two files.
    /// </summary>
    private static string Truncate(string name)
    {
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..8];
        var kept = _maximumLength - stamp.Length - 1;

        // Never cut mid-escape: a half-written '%2' would read as a literal, which says something
        // untrue about what was encoded. One step is enough - the character before an escape is
        // never part of another one.
        if (name.LastIndexOf(_escape, kept - 1) is int last && last >= kept - 2)
        {
            kept = last;
        }

        return $"{name[..kept]}-{stamp}";
    }
}
