using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// One derivative a registered version points at. A pointer, not ownership: the blob is shared by
/// every version whose artwork is byte-identical, which is most releases of the same mod.
/// </summary>
public record ModImageReference(string Hash, ModImageKind Kind, int Position, string FileName)
{
    public static ModImageReference FromDto(ModImageReferenceDto dto)
    {
        return new(dto.Hash, dto.Kind, dto.Position, dto.FileName);
    }

    public ModImageReferenceDto ToDto()
    {
        return new ModImageReferenceDto()
        {
            Hash = Hash,
            Kind = Kind,
            Position = Position,
            FileName = FileName
        };
    }
}
