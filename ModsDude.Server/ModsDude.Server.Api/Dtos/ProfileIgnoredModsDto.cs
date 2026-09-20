using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Dtos;

/// <param name="ModIds">Everything the profile ignores, in mod id order.</param>
public record ProfileIgnoredModsDto(IEnumerable<string> ModIds)
{
    public static ProfileIgnoredModsDto From(IEnumerable<ModId> modIds)
        => new([.. modIds.Select(x => x.Value).Order(StringComparer.Ordinal)]);
}
