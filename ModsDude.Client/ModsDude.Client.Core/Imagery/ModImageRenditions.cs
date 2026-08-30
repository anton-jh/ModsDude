using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// The sizes the two renditions are encoded at. The renditions themselves are
/// <see cref="ModImageRendition"/>, which is the server's enum: what a reference says it is and what
/// this client encodes have to be the same two values, or a client could publish a rendition the
/// repo has no name for.
/// </summary>
public static class ModImageRenditions
{
    public const int ThumbnailMaxEdge = 128;

    /// <summary>
    /// A safety net rather than a working limit: measured over 540 mods, no icon or store image
    /// reaches it, which is why the full rendition is a re-encode rather than a downscale. The
    /// saving is DDS to WebP.
    /// </summary>
    public const int FullMaxEdge = 1024;

    /// <summary>
    /// Every image is published at both, icons included. Storing an icon as a thumbnail alone would
    /// leave a details dialog for a mod that ships no store images drawing 128 px large — and
    /// storing it as a full alone would put ~50 KB behind every row of a cold list, which is the
    /// tenfold difference the thumbnail exists to buy.
    /// </summary>
    public static IReadOnlyList<ModImageRendition> All { get; } = [ModImageRendition.Full, ModImageRendition.Thumbnail];


    public static int GetMaxEdge(ModImageRendition rendition) => rendition switch
    {
        ModImageRendition.Thumbnail => ThumbnailMaxEdge,
        ModImageRendition.Full => FullMaxEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(rendition))
    };

    /// <summary>
    /// The size a rendition is encoded at. Never upscales - a 64 px icon stays 64 px, since
    /// inventing pixels only makes the file larger.
    /// </summary>
    public static (int Width, int Height) GetTargetSize(int width, int height, ModImageRendition rendition)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "An image with no area has no rendition.");
        }

        var maxEdge = GetMaxEdge(rendition);
        var longest = Math.Max(width, height);

        if (longest <= maxEdge)
        {
            return (width, height);
        }

        var scale = maxEdge / (double)longest;

        return (Scale(width, scale), Scale(height, scale));
    }


    /// <summary>
    /// Rounds rather than truncates, so a 1024x577 source keeps its shape at 128x72 instead of
    /// leaning a pixel narrower. The floor of one keeps a very wide image from losing its shorter
    /// edge entirely.
    /// </summary>
    private static int Scale(int edge, double scale)
    {
        return Math.Max(1, (int)Math.Round(edge * scale, MidpointRounding.AwayFromZero));
    }
}
