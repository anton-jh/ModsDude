using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// Carries <c>ContentHash</c> because sync reads a profile's dependencies rather than the repo's
/// mod list; without it here every sync would have to pull the unpaged mod list to resolve it.
/// </summary>
/// <param name="FileName">
/// What the file has to be called in the mod folder, in its registered casing. Carried here for the
/// same reason <paramref name="ContentHash"/> is: sync reads a profile's dependencies and nothing
/// else, so anything it needs per mod has to arrive with them.
/// </param>
public record ModDependencyDto(string ModId, string ModVersionId, string FileName, string ContentHash, bool Locked)
{
    public static ModDependencyDto FromModel(ModDependency model)
        => new(model.ModVersion.ModId.Value, model.ModVersion.Id.Value, model.ModVersion.FileName, model.ModVersion.ContentHash, model.Locked);
}
