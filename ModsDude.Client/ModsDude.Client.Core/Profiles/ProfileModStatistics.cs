using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What one revision of a profile's mod list adds up to: how many mods, and how many bytes they are.
/// </summary>
public sealed record ProfileModStatistics(int Revision, int ModCount, long Bytes)
{
    public static ProfileModStatistics From(GetModDependenciesResponse response)
    {
        var list = response.Dependencies.ToList();

        return new ProfileModStatistics(response.Revision, list.Count, list.Sum(x => x.SizeBytes));
    }
}
