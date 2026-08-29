using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <summary>
/// Turns one image out of a mod archive into the renditions the repo stores.
/// </summary>
/// <remarks>
/// This runs on the client because only the client can read what mods actually ship: DDS, over half
/// of it BC7, which needs the managed decoder because WIC refuses it. The server has no image stack
/// and no business opening mod files.
/// </remarks>
internal static class ModImageDerivativeGenerator
{
    public static async Task<GeneratedModImage> GenerateAsync(
        ModImage image,
        ModImageKind kind,
        int index,
        CancellationToken cancellationToken)
    {
        var data = await image.Load(cancellationToken);

        return await Task.Run(() =>
        {
            var decoded = ModImageDecoder.DecodeToPixels(data);

            var renditions = ModImageReferenceLayout.GetDerivatives(kind)
                .Select(x => Encode(decoded, x))
                .ToList();

            return new GeneratedModImage(kind, index, image.Name, renditions);
        }, cancellationToken);
    }


    private static GeneratedRendition Encode(DecodedImage decoded, ModImageDerivative derivative)
    {
        var (width, height) = ModImageDerivatives.GetTargetSize(decoded.Width, decoded.Height, derivative);

        var bytes = WebPCodec.Encode(decoded.Bgra, decoded.Width, decoded.Height, width, height);

        return new GeneratedRendition(derivative, ModImageHashing.Compute(bytes), bytes);
    }
}


internal record GeneratedModImage(ModImageKind Kind, int Index, string FileName, IReadOnlyList<GeneratedRendition> Renditions);

/// <param name="Hash">The address these bytes belong at, which is a hash of the bytes themselves.</param>
internal record GeneratedRendition(ModImageDerivative Derivative, string Hash, byte[] Bytes);
