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
/// <param name="SizeBytes">
/// Carried for the same reason: what an apply will download is the sum of the sizes of what it has not
/// got, and that has to be knowable from the dependency list alone. Null where the version predates the
/// size being recorded and has not been backfilled yet.
/// </param>
public record ModDependencyDto(string ModId, string ModVersionId, string FileName, string ContentHash, long? SizeBytes, bool Locked)
{
    public static ModDependencyDto FromModel(ModDependency model)
        => new(model.ModVersion.ModId.Value, model.ModVersion.Id.Value, model.ModVersion.FileName, model.ModVersion.ContentHash, model.ModVersion.SizeBytes, model.Locked);
}
