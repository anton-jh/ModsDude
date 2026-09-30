using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;

namespace ModsDude.Client.Core.Tests.GameProcesses;

public class GameRunningGuardTests
{
    [Fact]
    public void A_running_game_is_refused_by_name()
    {
        var guard = new GameRunningGuard(new Names(["FarmingSimulator2025Game"]), new Processes(running: true));

        var refusal = Assert.Throws<GameRunningException>(() => guard.EnsureNotRunning(Keys.Game(), "Farming Simulator 25"));

        Assert.Equal("Farming Simulator 25", refusal.GameName);
        Assert.Contains("Farming Simulator 25", refusal.UserMessage);
    }

    [Fact]
    public void A_game_that_is_not_running_is_let_through()
    {
        var guard = new GameRunningGuard(new Names(["FarmingSimulator2025Game"]), new Processes(running: false));

        guard.EnsureNotRunning(Keys.Game(), "Farming Simulator 25");
    }


    private sealed class Names(IReadOnlyList<string> names) : IGameProcessNames
    {
        public IReadOnlyList<string> Get(GameIdentity game) => names;
    }

    private sealed class Processes(bool running) : IGameProcesses
    {
        public bool IsAnyRunning(IReadOnlyList<string> processNames) => running && processNames.Count > 0;
    }
}
