using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What a set of savegames adds up to: how many, how many snapshots they carry, and how many bytes.
/// </summary>
/// <param name="TotalBytes">
/// Counted per snapshot, as the server reports it: snapshots that share a blob are counted twice. It is
/// how much history the saves carry rather than what storage holds, and pruning is what brings it down.
/// </param>
public sealed record SavegameStatistics(int Savegames, int Snapshots, long TotalBytes)
{
    public static SavegameStatistics From(IEnumerable<SavegameDto> savegames)
    {
        var list = savegames.ToList();

        return new SavegameStatistics(
            list.Count,
            list.Sum(x => x.SnapshotCount),
            list.Sum(x => x.TotalSizeBytes));
    }
}
