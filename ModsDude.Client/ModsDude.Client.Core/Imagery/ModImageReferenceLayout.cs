using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// How two derivatives of one source image fit into a reference set that addresses one blob per
/// (kind, position).
/// </summary>
/// <remarks>
/// <para>
/// A store image occupies two adjacent positions - <c>index * 2</c> for the full rendition and one
/// past it for the thumbnail. Any other split would have to be read against the size of the set,
/// which imagery cannot rely on: it arrives late, in unknown completeness, and from more than one
/// uploader, so a subset has to decode to the same thing as the whole.
/// </para>
/// <para>
/// The icon is the exception, and not by choice: the server accepts at most one reference of kind
/// Icon, so an icon has a thumbnail and nothing else. That is what an icon is actually used for -
/// 64 px in a list row, 96 px in the strip - and the alternative, pointing the single icon
/// reference at the full rendition, would put ~50 KB behind every row of a cold list and give back
/// exactly the ten-fold difference the thumbnail exists to buy. The visible cost is a details
/// dialog for a mod that ships no store images, where the icon is drawn large from 128 px.
/// </para>
/// </remarks>
public static class ModImageReferenceLayout
{
    public static int GetPosition(int index, ModImageDerivative derivative)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (index * 2) + (derivative is ModImageDerivative.Thumbnail ? 1 : 0);
    }

    public static int GetIndex(int position) => position / 2;

    public static ModImageDerivative GetDerivative(int position)
    {
        return position % 2 == 0 ? ModImageDerivative.Full : ModImageDerivative.Thumbnail;
    }

    /// <summary>Which renditions are worth generating for a kind, in the order they are uploaded.</summary>
    public static IReadOnlyList<ModImageDerivative> GetDerivatives(ModImageKind kind)
    {
        return kind is ModImageKind.Icon
            ? [ModImageDerivative.Thumbnail]
            : [ModImageDerivative.Full, ModImageDerivative.Thumbnail];
    }

    public static ModImageReference CreateReference(ModImageKind kind, int index, ModImageDerivative derivative, string hash, string fileName)
    {
        // One icon means one position, so the icon's reference cannot carry the derivative in its
        // position the way a store image's does.
        var position = kind is ModImageKind.Icon
            ? 0
            : GetPosition(index, derivative);

        return new ModImageReference(hash, kind, position, fileName);
    }
}
