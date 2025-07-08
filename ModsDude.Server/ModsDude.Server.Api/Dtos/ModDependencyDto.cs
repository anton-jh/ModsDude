using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

public record ModDependencyDto(string ModId, string ModVersionId, bool LockVersion)
{
    public static ModDependencyDto FromModel(ModDependency model)
        => new(model.ModVersion.Mod.Id.Value, model.ModVersion.Id.Value, model.LockVersion);
}
