using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

public record ModDto(
    string Id,
    IEnumerable<ModVersionDto> Versions,
    DateTimeOffset Created,
    DateTimeOffset Updated)
{
    public static ModDto FromModel(Mod model)
    {
        return new ModDto(
            model.Id.Value,
            model.Versions.Select(ModVersionDto.FromModel),
            model.Created,
            model.Updated);
    }
}


public record ModVersionDto(
    string VersionId,
    int SequenceNumber,
    string DisplayName,
    string Description,
    IEnumerable<ModAttributeDto> Attributes,
    DateTimeOffset Created)
{
    public static ModVersionDto FromModel(ModVersion model)
    {
        return new ModVersionDto(
            model.Id.Value,
            model.SequenceNumber,
            model.DisplayName,
            model.Description,
            model.Attributes.Select(ModAttributeDto.FromModel),
            model.Created);
    }
}
