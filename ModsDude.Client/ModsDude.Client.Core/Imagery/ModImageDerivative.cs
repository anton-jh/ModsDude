namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// Which of the two renditions a derivative is. They match the two ways mod imagery is actually
/// consumed, and nothing else is stored.
/// </summary>
public enum ModImageDerivative
{
    /// <summary>What list rows and the details strip draw. ~6 KB, and the reason a cold list costs megabytes rather than tens of megabytes.</summary>
    Thumbnail,

    /// <summary>What somebody opening one image to look at it gets.</summary>
    Full
}


public static class ModImageDerivatives
{
    public const int ThumbnailMaxEdge = 128;

    /// <summary>
    /// A safety net rather than a working limit: measured over 540 mods, no icon or store image
    /// reaches it, which is why the full rendition is a re-encode rather than a downscale. The
    /// saving is DDS to WebP.
    /// </summary>
    public const int FullMaxEdge = 1024;


    public static int GetMaxEdge(ModImageDerivative derivative) => derivative switch
    {
        ModImageDerivative.Thumbnail => ThumbnailMaxEdge,
        ModImageDerivative.Full => FullMaxEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(derivative))
    };

    /// <summary>
    /// The size a rendition is encoded at. Never upscales - a 64 px icon stays 64 px, since
    /// inventing pixels only makes the file larger.
    /// </summary>
    public static (int Width, int Height) GetTargetSize(int width, int height, ModImageDerivative derivative)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "An image with no area has no rendition.");
        }

        var maxEdge = GetMaxEdge(derivative);
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
