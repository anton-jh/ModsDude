namespace ModsDude.Client.Core.Models;

/// <summary>
/// A mod's identity, in the one casing the system is allowed to use.
/// </summary>
/// <remarks>
/// The id originates from an archive's filename, so it arrives in whatever casing the file happens
/// to have. Windows treats 'FS25_MyMod.zip' and 'FS25_mymod.zip' as the same file; Azure blob names
/// and the server's '(RepoId, ModId)' key are both case-sensitive, so two members would register
/// two mods pointing at two blobs for what is one mod to them. A type with a private constructor
/// makes the normalized form the only representable one, so no path can carry an un-normalized id
/// far enough to reach storage. See docs/09-mod-catalog.md#the-casing-trap.
/// </remarks>
public readonly record struct ModKey
{
    private readonly string? _value;


    private ModKey(string value)
    {
        _value = value;
    }


    public string Value => _value ?? string.Empty;


    /// <summary>Normalizes a raw id. Call this where the id enters the client, not at each use site.</summary>
    public static ModKey From(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("A mod id cannot be empty.", nameof(raw));
        }

        return new(normalized);
    }

    public override string ToString() => Value;
}
