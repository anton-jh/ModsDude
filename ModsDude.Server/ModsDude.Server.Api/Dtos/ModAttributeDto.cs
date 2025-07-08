using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

public record ModAttributeDto(string Key, string? Value)
{
    public static ModAttribute ToModel(ModAttributeDto dto)
    {
        return new ModAttribute(dto.Key, dto.Value);
    }

    public static ModAttributeDto FromModel(ModAttribute model)
    {
        return new ModAttributeDto(model.Key, model.Value);
    }
};
