using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// One DTO per version, with no parent. Nesting versions under a mod would only make the client
/// re-group on receipt, which is exactly what its flat model exists to avoid.
/// </summary>
public record ModDto(
    string ModId,
    string VersionId,
    int SequenceNumber,
    string DisplayName,
    string Description,
    string ContentHash,
    bool Locked,
    IEnumerable<ModAttributeDto> Attributes,
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
            model.ContentHash,
            model.Locked,
            model.Attributes.Select(ModAttributeDto.FromModel),
            model.Created,
            model.Updated);
    }
}
