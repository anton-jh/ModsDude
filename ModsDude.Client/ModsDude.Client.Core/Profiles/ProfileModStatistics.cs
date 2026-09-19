using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What a profile's mod list adds up to: how many mods, and how many bytes they are.
/// </summary>
/// <param name="KnownBytes">
/// The sum of the sizes the repo could give. A lower bound while <paramref name="UnknownSizeCount"/> is
/// above zero - a version registered before sizes were recorded, and not yet backfilled, is uncounted
/// rather than counted as empty.
/// </param>
public sealed record ProfileModStatistics(int ModCount, long KnownBytes, int UnknownSizeCount)
{
    public static ProfileModStatistics From(IEnumerable<ModDependencyDto> dependencies)
    {
        var list = dependencies.ToList();

        return new ProfileModStatistics(
            list.Count,
            list.Sum(x => x.SizeBytes ?? 0),
            list.Count(x => x.SizeBytes is null));
    }

    public bool IsSizeComplete => UnknownSizeCount == 0;
}
