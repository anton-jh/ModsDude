using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// One DTO per version, with no parent. Nesting versions under a mod would only make the client
/// re-group on receipt, which is exactly what its flat model exists to avoid.
/// </summary>
/// <param name="FileName">
/// What the archive is called on disk, in its registered casing. Distinct from
/// <paramref name="DisplayName"/>, which is the mod's title: this is the name a client writes.
/// </param>
public record ModDto(
    string ModId,
    string VersionId,
    int SequenceNumber,
    string DisplayName,
    string Description,
    string FileName,
    string ContentHash,
    bool Locked,
    IEnumerable<ModAttributeDto> Attributes,
    IEnumerable<ModImageReferenceDto> Images,
    DateTimeOffset Created,
    DateTimeOffset Updated)
{
    public static ModDto FromModel(ModVersion model)
    {
        return new ModDto(
            model.ModId.Value,
            model.Id.Value,
            model.SequenceNumber,
            model.DisplayName,
            model.Description,
            model.FileName,
            model.ContentHash,
            model.Locked,
            model.Attributes.Select(ModAttributeDto.FromModel),
            model.Images.OrderBy(x => x.Kind).ThenBy(x => x.Position).ThenBy(x => x.Rendition).Select(ModImageReferenceDto.FromModel),
            model.Created,
            model.Updated);
    }
}
