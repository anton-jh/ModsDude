using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// A pointer, not ownership: the blob at <paramref name="Hash"/> is shared by every version whose
/// imagery happens to be identical, which is most releases of the same mod.
/// </summary>
public record ModImageReferenceDto(string Hash, ModImageKind Kind, int Position, string FileName)
{
    public static ModImageReference ToModel(ModImageReferenceDto dto)
    {
        return new ModImageReference(dto.Hash, dto.Kind, dto.Position, dto.FileName);
    }

    public static ModImageReferenceDto FromModel(ModImageReference model)
    {
        return new ModImageReferenceDto(model.Hash, model.Kind, model.Position, model.FileName);
    }
}
