using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

/// <param name="ModIds">Everything the profile ignores, in mod id order.</param>
/// <param name="Version">What a change to the list sends back, to say which list it was made against.</param>
public record ProfileIgnoredModsDto(IEnumerable<string> ModIds, int Version)
{
    public static ProfileIgnoredModsDto From(IEnumerable<ModId> modIds, int version)
        => new([.. modIds.Select(x => x.Value).Order(StringComparer.Ordinal)], version);
}
