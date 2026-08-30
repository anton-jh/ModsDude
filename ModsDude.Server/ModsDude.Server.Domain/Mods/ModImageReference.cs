namespace ModsDude.Server.Domain.Mods;

public class ModImageReference(
    string hash,
    ModImageKind kind,
    ModImageRendition rendition,
    int position,
    string fileName)
{
    public string Hash { get; init; } = ModImageHash.Validated(hash);
    public ModImageKind Kind { get; init; } = kind;
    public ModImageRendition Rendition { get; init; } = rendition;

    /// <summary>
    /// Where the source image sits in the mod's own ordered list of images of this kind — not which
    /// rendition it is, which <see cref="Rendition"/> now carries. The two renditions of one image
    /// therefore share a position, which is what lets a half-published pair still be recognised as
    /// one image.
    /// </summary>
    public int Position { get; init; } = position;

    public string FileName { get; init; } = fileName;
}


public enum ModImageKind
{
    Icon,
    StoreImage
}


/// <summary>
/// Which of the two derivatives a reference points at. Structural rather than a
/// <see cref="ModAttribute"/>: the system dereferences it to decide what to draw at what size, which
/// is the same rule that put <see cref="ModVersion.ContentHash"/> and <see cref="ModVersion.Locked"/>
/// in the schema.
/// </summary>
public enum ModImageRendition
{
    /// <summary>128 px longest edge. What list rows and the details strip draw.</summary>
    Thumbnail,

    /// <summary>Native resolution capped at 1024 px. What somebody opening one image to look at it gets.</summary>
    Full
}
