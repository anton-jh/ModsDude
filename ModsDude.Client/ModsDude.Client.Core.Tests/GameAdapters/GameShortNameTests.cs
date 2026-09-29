using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// What a game is called where its name does not fit - the heading over its repos in a sidebar rail.
/// </summary>
public class GameShortNameTests
{
    [Theory]
    [InlineData(FarmingSimulatorGameVersion.Fs25, "FS25")]
    public void Farming_Simulator_is_named_for_its_game_version(FarmingSimulatorGameVersion version, string expected)
    {
        var adapter = new FarmingSimulatorBaseGameAdapter(new FarmingSimulatorBaseSettings { GameVersion = version });

        Assert.Equal(expected, adapter.GameShortName);
    }

    [Fact]
    public void Farming_Simulator_without_a_game_version_is_the_series()
    {
        var adapter = new FarmingSimulatorBaseGameAdapter(new FarmingSimulatorBaseSettings());

        Assert.Equal("FS", adapter.GameShortName);
    }

    [Fact]
    public void Every_game_version_has_a_short_name_of_its_own()
    {
        var names = Enum.GetValues<FarmingSimulatorGameVersion>()
            .Select(x => new FarmingSimulatorBaseGameAdapter(new FarmingSimulatorBaseSettings { GameVersion = x }).GameShortName)
            .ToList();

        Assert.DoesNotContain("FS", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
