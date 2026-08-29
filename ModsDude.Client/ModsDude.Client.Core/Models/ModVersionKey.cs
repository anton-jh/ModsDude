namespace ModsDude.Client.Core.Models;

/// <summary>
/// A version's identity within its mod - 'modDesc/version', which is what the server stores as
/// 'VersionId'.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> case-folded, unlike <see cref="ModKey"/>. The casing trap is a property
/// of filenames: the filesystem hands back whatever casing a file has, so the same mod yields two
/// ids. A version string comes out of the archive's own modDesc.xml and is therefore byte-identical
/// for everyone holding that file, so folding it would only destroy the casing the author chose and
/// the row displays. It is a type rather than a bare string so that it cannot be swapped with a
/// <see cref="ModKey"/> at a call site.
/// </remarks>
public readonly record struct ModVersionKey
{
    private readonly string? _value;


    private ModVersionKey(string value)
    {
        _value = value;
    }


    public string Value => _value ?? string.Empty;


    public static ModVersionKey From(string raw)
    {
        var normalized = raw.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("A mod version cannot be empty.", nameof(raw));
        }

        return new(normalized);
    }

    public override string ToString() => Value;
}
