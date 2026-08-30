using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// One derivative a registered version points at. A pointer, not ownership: the blob is shared by
/// every version whose artwork is byte-identical, which is most releases of the same mod.
/// </summary>
/// <param name="Position">
/// Where the source image sits in the mod's own ordered list of images of this kind. The two
/// renditions of one image share it, which is what identifies them as one image - including when
/// only one of the two made it up, since imagery arrives late, incomplete, and from more than one
/// uploader.
/// </param>
public record ModImageReference(string Hash, ModImageKind Kind, ModImageRendition Rendition, int Position, string FileName)
{
    public static ModImageReference FromDto(ModImageReferenceDto dto)
    {
        return new(dto.Hash, dto.Kind, dto.Rendition, dto.Position, dto.FileName);
    }

    public ModImageReferenceDto ToDto()
    {
        return new ModImageReferenceDto()
        {
            Hash = Hash,
            Kind = Kind,
            Rendition = Rendition,
            Position = Position,
            FileName = FileName
        };
    }
}
