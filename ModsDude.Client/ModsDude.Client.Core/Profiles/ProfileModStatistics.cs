using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What a profile's mod list adds up to: how many mods, and how many bytes they are.
/// </summary>
public sealed record ProfileModStatistics(int ModCount, long Bytes)
{
    public static ProfileModStatistics From(IEnumerable<ModDependencyDto> dependencies)
    {
        var list = dependencies.ToList();

        return new ProfileModStatistics(list.Count, list.Sum(x => x.SizeBytes));
    }
}
