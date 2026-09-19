using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameStatisticsTests
{
    [Fact]
    public void A_repos_saves_add_up_their_snapshots_and_their_bytes()
    {
        var statistics = SavegameStatistics.From([Savegame(3, 3000), Savegame(1, 500), Savegame(0, 0)]);

        Assert.Equal(new SavegameStatistics(3, 4, 3500), statistics);
    }

    [Fact]
    public void A_repo_with_no_saves_adds_up_to_nothing()
    {
        Assert.Equal(new SavegameStatistics(0, 0, 0), SavegameStatistics.From([]));
    }


    private static SavegameDto Savegame(int snapshots, long bytes) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Save",
        SnapshotCount = snapshots,
        TotalSizeBytes = bytes
    };
}
