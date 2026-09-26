using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Tests.GameAdapters;

namespace ModsDude.Client.Core.Tests.Games;

/// <summary>
/// Which games are connected without a connect page: exactly those whose local settings have nothing
/// on them to fill in.
/// </summary>
public class AutoConnectTests
{
    [Fact]
    public void Farming_Simulator_has_nothing_to_ask_so_it_connects_by_itself()
    {
        var adapter = new FarmingSimulatorGameAdapter()
            .WithBaseSettings(new FarmingSimulatorBaseSettings { GameVersion = FarmingSimulatorGameVersion.Fs25 });

        Assert.True(GameRepository.ConnectsAutomatically(adapter));
    }

    [Fact]
    public void A_game_with_folders_to_pick_is_connected_from_its_page()
    {
        Assert.False(GameRepository.ConnectsAutomatically(new FakeMultiTargetBaseGameAdapter()));
    }
}
