namespace ModsDude.Client.Core.Models;

/// <summary>
/// One mod archive found on disk - already one record per version, since one file is one version.
/// </summary>
/// <param name="Id">
/// Normalized by the adapter that produced it. The type is what stops an un-normalized id reaching
/// the server; see <see cref="ModKey"/>.
/// </param>
/// <param name="GetStream">Opens the mod file itself, for upload.</param>
public record LocalMod(ModKey Id, ModVersionKey Version, string Name, string Description, Func<Stream> GetStream)
{
    /// <summary>The archive this was read from. Names which file a source contributed.</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The archive's size. The only content signal a scan can afford - hashing every archive in a
    /// mod folder would read tens of gigabytes - so it is what tells two sources claiming the same
    /// mod and version apart. See docs/09-mod-catalog.md#same-mod-several-sources.
    /// </summary>
    public required long FileLength { get; init; }

    /// <summary>
    /// Small square image used in list rows. Null when the mod doesn't ship one.
    /// </summary>
    public ModImage? Icon { get; init; }

    /// <summary>
    /// Larger presentation images for the details view. Frequently empty - script-only mods
    /// usually ship nothing but an icon.
    /// </summary>
    public IReadOnlyList<ModImage> Images { get; init; } = [];

    public string? Author { get; init; }
}
