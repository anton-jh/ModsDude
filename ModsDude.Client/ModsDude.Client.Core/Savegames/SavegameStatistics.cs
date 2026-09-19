using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What a set of savegames adds up to: how many, how many snapshots they carry, and how many bytes.
/// </summary>
/// <param name="TotalBytes">
/// What storage holds for the saves, as the server reports it: counted per blob, so snapshots that share
/// one are counted once. Pruning is what brings it down.
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
