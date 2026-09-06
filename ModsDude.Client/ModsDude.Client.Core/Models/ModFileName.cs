namespace ModsDude.Client.Core.Models;

/// <summary>
/// What a mod's file is called on disk - the spelling, where <see cref="ModKey"/> is the identity.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModKey"/> lower-cases the id because it becomes a blob path segment and half of the
/// server's key, both case-sensitive. That normalization used to decide the filename too: the
/// adapter rebuilt the name out of the id, so applying a profile renamed every archive in the mod
/// folder to lower case. This carries the name the file actually has, so the id can stay normalized
/// and the folder can still read the way the user's downloads do.
/// See docs/09-mod-catalog.md#the-casing-trap.
/// </para>
/// <para>
/// A value that came off <em>somebody else's</em> disk, through the repo, and is about to be
/// interpolated into a path here - so it is checked rather than trusted, and a private constructor
/// makes the checked form the only representable one, exactly as with <see cref="ModKey"/>. Valid
/// means a bare file name - no separator, no traversal, nothing a path normalizer would rewrite -
/// that <b>belongs to its mod</b>: its stem normalizes to the same <see cref="ModKey"/>. So a repo
/// can decide how a mod's own file is spelled, and nothing else.
/// </para>
/// </remarks>
public readonly record struct ModFileName
{
    /// <summary>Long enough for any real archive; a Windows path component cannot exceed it.</summary>
    private const int _maxLength = 255;

    private static readonly char[] _forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private readonly string? _value;


    private ModFileName(string value)
    {
        _value = value;
    }


    public string Value => _value ?? string.Empty;


    /// <summary>
    /// The name to register for a mod file at <paramref name="path"/>. Never fails: a name that
    /// cannot be carried falls back to the one the id alone produces, which is what the adapter
    /// built before any of this existed.
    /// </summary>
    /// <remarks>
    /// The fallbacks are defensive rather than expected. A file the adapter has just opened came
    /// from a real directory entry, so its name is already a bare, well-formed one, and its stem is
    /// where <paramref name="modId"/> came from in the first place.
    /// </remarks>
    public static ModFileName ForFile(ModKey modId, string path)
    {
        var name = Path.GetFileName(path);

        return For(modId, name)
            ?? For(modId, modId.Value + Path.GetExtension(name))
            ?? new(modId.Value);
    }

    /// <summary>The name if it can be written as this mod's file, and null if it cannot.</summary>
    public static ModFileName? For(ModKey modId, string? raw)
    {
        if (IsWellFormed(raw) is false)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(raw!);

        if (string.IsNullOrWhiteSpace(stem) || ModKey.From(stem) != modId)
        {
            return null;
        }

        return new(raw!);
    }

    public override string ToString() => Value;


    private static bool IsWellFormed(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > _maxLength)
        {
            return false;
        }

        // Windows strips a trailing space or dot when it normalizes a path, so a name carrying one
        // could never be written as it reads - which is the only reason to carry it at all. This is
        // also what rejects '.' and '..'. A leading space is left alone: it survives being written,
        // so it is somebody's odd filename rather than an unwritable one.
        if (raw != raw.TrimEnd() || raw.EndsWith('.'))
        {
            return false;
        }

        return raw.IndexOfAny(_forbidden) < 0 && raw.Any(char.IsControl) is false;
    }
}
