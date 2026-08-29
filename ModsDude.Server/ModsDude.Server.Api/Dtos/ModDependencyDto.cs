using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// Carries <c>ContentHash</c> because sync reads a profile's dependencies rather than the repo's
/// mod list; without it here every sync would have to pull the unpaged mod list to resolve it.
/// </summary>
public record ModDependencyDto(string ModId, string ModVersionId, string ContentHash, bool Locked)
{
    public static ModDependencyDto FromModel(ModDependency model)
        => new(model.ModVersion.ModId.Value, model.ModVersion.Id.Value, model.ModVersion.ContentHash, model.Locked);
}
