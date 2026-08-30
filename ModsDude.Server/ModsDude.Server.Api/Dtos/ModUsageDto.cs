using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// How many of a repo's profiles pin one registered version.
/// </summary>
/// <remarks>
/// Its own resource rather than a field on <see cref="ModDto"/>, because usage changes for reasons a
/// version does not. The mod list's delta form is keyed on <c>ModVersion.Updated</c>; carrying usage
/// on the version would mean either serving stale usage to every client that syncs incrementally, or
/// restamping every version a profile touches — which for a profile of two thousand mods restamps
/// two thousand rows on every save and makes the delta the same size as a full listing.
/// </remarks>
public record ModUsageDto(string ModId, string VersionId, int ProfileCount)
{
    public static ModUsageDto FromModel(ModVersionUsage model)
    {
        return new ModUsageDto(model.ModId.Value, model.VersionId.Value, model.ProfileCount);
    }
}
