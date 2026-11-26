using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.GameProcesses;

public class GameRunningMonitorTests
{
    private static readonly GameIdentity _fs25 = GameIdentity.Parse("_farming_simulator#fs25");
    private static readonly GameIdentity _beam = GameIdentity.Parse("_beamng");


    [Fact]
    public void Nothing_runs_before_the_first_look()
    {
        var harness = new Harness();
        harness.Alive.Add("FarmingSimulator2025Game");

        Assert.False(harness.Monitor.HasPolled);
        Assert.False(harness.Monitor.IsRunning(_fs25));
    }

    [Fact]
    public void A_game_with_a_live_process_is_running()
    {
        var harness = new Harness();
        harness.Alive.Add("FarmingSimulator2025Game");

        harness.Monitor.Poll();

        Assert.True(harness.Monitor.IsRunning(_fs25));
        Assert.False(harness.Monitor.IsRunning(_beam));
    }

    [Fact]
    public void A_game_whose_adapter_names_no_process_is_never_running()
    {
        var harness = new Harness();
        harness.Names[_fs25] = [];
        harness.Alive.Add("FarmingSimulator2025Game");

        harness.Monitor.Poll();

        Assert.False(harness.Monitor.IsRunning(_fs25));
    }

    [Fact]
    public void Changed_is_raised_only_when_a_game_starts_or_stops()
    {
        var harness = new Harness();

        harness.Monitor.Poll();
        harness.Alive.Add("BeamNG.drive.x64");
        harness.Monitor.Poll();
        harness.Monitor.Poll();
        harness.Alive.Clear();
        harness.Monitor.Poll();

        Assert.Equal(2, harness.Raised);
    }

    [Fact]
    public void Running_games_come_in_identity_order()
    {
        var harness = new Harness();
        harness.Alive.Add("FarmingSimulator2025Game");
        harness.Alive.Add("BeamNG.drive.x64");

        harness.Monitor.Poll();

        Assert.Equal([_beam, _fs25], harness.Monitor.Running);
    }


    [Fact]
    public void A_game_that_was_already_running_at_the_first_look_has_not_started()
    {
        var harness = new Harness();
        harness.Alive.Add("FarmingSimulator2025Game");

        Assert.Empty(harness.Monitor.Poll());
        Assert.Empty(harness.Monitor.Poll());
    }

    [Fact]
    public void A_game_that_starts_between_looks_is_reported_once()
    {
        var harness = new Harness();
        harness.Monitor.Poll();

        harness.Alive.Add("FarmingSimulator2025Game");

        Assert.Equal([_fs25], harness.Monitor.Poll());
        Assert.Empty(harness.Monitor.Poll());
    }

    [Fact]
    public void A_game_that_stops_and_starts_again_is_reported_again()
    {
        var harness = new Harness();
        harness.Monitor.Poll();
        harness.Alive.Add("FarmingSimulator2025Game");
        harness.Monitor.Poll();
        harness.Alive.Clear();
        harness.Monitor.Poll();

        harness.Alive.Add("FarmingSimulator2025Game");

        Assert.Equal([_fs25], harness.Monitor.Poll());
    }

    [Fact]
    public void Games_starting_in_one_look_come_in_identity_order()
    {
        var harness = new Harness();
        harness.Monitor.Poll();

        harness.Alive.Add("FarmingSimulator2025Game");
        harness.Alive.Add("BeamNG.drive.x64");

        Assert.Equal([_beam, _fs25], harness.Monitor.Poll());
    }

    /// <summary>Connecting a game that is already running is not watching it start.</summary>
    [Fact]
    public void A_game_connected_while_it_runs_has_not_started()
    {
        var harness = new Harness();
        harness.Connected.Remove(_fs25);
        harness.Monitor.Poll();

        harness.Alive.Add("FarmingSimulator2025Game");
        harness.Connected.Add(_fs25);

        Assert.Empty(harness.Monitor.Poll());
        Assert.True(harness.Monitor.IsRunning(_fs25));
    }

    private sealed class Harness : IDriftCandidateSource, IGameProcessNames, IGameProcesses
    {
        public Harness()
        {
            Monitor = new GameRunningMonitor(this, this, this);
            Monitor.Changed += (_, _) => Raised++;
        }

        public GameRunningMonitor Monitor { get; }
        public int Raised { get; private set; }
        public HashSet<string> Alive { get; } = [];

        public Dictionary<GameIdentity, IReadOnlyList<string>> Names { get; } = new()
        {
            [_fs25] = ["FarmingSimulator2025Game"],
            [_beam] = ["BeamNG.drive.x64"]
        };

        public HashSet<GameIdentity> Connected { get; } = [_fs25, _beam];

        public IReadOnlyList<DriftCandidate> GetDriftCandidates()
            => [.. Connected.OrderBy(x => x.ToString(), StringComparer.Ordinal).Select(x => new DriftCandidate(x, x.ToString(), [], null))];

        public IReadOnlyList<string> Get(GameIdentity game) => Names[game];

        public bool IsAnyRunning(IReadOnlyList<string> processNames) => processNames.Any(Alive.Contains);
    }
}
