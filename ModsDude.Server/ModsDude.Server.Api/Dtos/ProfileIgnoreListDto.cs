using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Dtos;

/// <param name="ModIds">Every mod the profile ignores, in mod id order.</param>
/// <param name="DismissedVersions">Every version it is not offered as an update, in mod id then version id order.</param>
/// <param name="Version">What a change to the list sends back, to say which list it was made against.</param>
public record ProfileIgnoreListDto(IEnumerable<string> ModIds, IEnumerable<DismissedVersionDto> DismissedVersions, int Version)
{
    public static ProfileIgnoreListDto From(ProfileIgnoreList list, int version)
        => new(
            [.. list.Mods.Select(x => x.Value).Order(StringComparer.Ordinal)],
            [.. list.Versions
                .OrderBy(x => x.ModId.Value, StringComparer.Ordinal)
                .ThenBy(x => x.VersionId.Value, StringComparer.Ordinal)
                .Select(DismissedVersionDto.From)],
            version);
}

public record DismissedVersionDto(string ModId, string VersionId)
{
    public static DismissedVersionDto From(DismissedVersion version) => new(version.ModId.Value, version.VersionId.Value);
}
