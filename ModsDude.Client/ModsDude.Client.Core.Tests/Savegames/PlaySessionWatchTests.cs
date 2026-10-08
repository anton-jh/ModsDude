using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// When closing the game is worth a reminder to check a savegame in.
/// </summary>
public class PlaySessionWatchTests
{
    private const string CheckedOut = "aaaa";


    [Fact]
    public async Task A_machine_holding_nothing_tracks_no_session()
    {
        var harness = new Harness();
        harness.Processes.Running = true;

        await harness.Poll();

        Assert.False(harness.Watch.IsRunning(Keys.Game()));
    }

    [Fact]
    public async Task Closing_the_game_after_playing_a_checked_out_save_reminds_about_it()
    {
        var harness = new Harness();
        harness.Hold();

        await harness.Poll();
        harness.Processes.Running = true;
        await harness.Poll();

        harness.Held.Hash = "bbbb";
        harness.Processes.Running = false;

        var played = Assert.Single(await harness.Poll());

        Assert.Equal(harness.SavegameId, played.SavegameId);
        Assert.Equal("My farm", played.SlotDisplayName);
        Assert.Single(harness.Raised);
    }

    [Fact]
    public async Task The_game_counts_as_running_until_the_session_has_been_read()
    {
        var harness = new Harness();
        harness.Hold();

        await harness.Poll();
        harness.Processes.Running = true;
        await harness.Poll();

        Assert.True(harness.Watch.IsRunning(Keys.Game()));

        harness.Processes.Running = false;
        await harness.Poll();

        Assert.False(harness.Watch.IsRunning(Keys.Game()));
    }

    [Fact]
    public async Task Closing_the_game_without_playing_the_save_says_nothing()
    {
        var harness = new Harness();
        harness.Hold();

        await harness.Poll();
        harness.Processes.Running = true;
        await harness.Poll();
        harness.Processes.Running = false;

        Assert.Empty(await harness.Poll());
        Assert.Empty(harness.Raised);
    }

    /// <summary>
    /// Play from an earlier evening is the drift notice's to say. This session was spent somewhere
    /// else, so this save is not what closing the game should be about.
    /// </summary>
    [Fact]
    public async Task Play_from_before_the_session_is_not_reminded_about()
    {
        var harness = new Harness();
        harness.Hold();
        harness.Held.Hash = "bbbb";

        await harness.Poll();
        harness.Processes.Running = true;
        await harness.Poll();
        harness.Processes.Running = false;

        Assert.Empty(await harness.Poll());
    }

    /// <summary>
    /// A <em>keep playing</em> check-in halfway through moves the check-out to what was just played,
    /// and nothing since is nothing to remind about.
    /// </summary>
    [Fact]
    public async Task A_save_checked_in_during_the_session_is_not_reminded_about()
    {
        var harness = new Harness();
        harness.Hold();

        await harness.Poll();
        harness.Processes.Running = true;
        await harness.Poll();

        harness.Held.Hash = "bbbb";
        harness.Hold(contentHash: "bbbb");
        harness.Processes.Running = false;

        Assert.Empty(await harness.Poll());
    }

    /// <summary>
    /// There is no start of the session to compare with, so it is measured from the check-out -
    /// whatever moved since then exists on this disk alone.
    /// </summary>
    [Fact]
    public async Task A_game_already_running_when_the_app_started_is_measured_from_the_check_out()
    {
        var harness = new Harness();
        harness.Hold();
        harness.Held.Hash = "bbbb";
        harness.Processes.Running = true;

        await harness.Poll();
        harness.Processes.Running = false;

        Assert.Single(await harness.Poll());
    }

    [Fact]
    public async Task An_unreadable_slot_says_nothing()
    {
        var harness = new Harness();
        harness.Hold();

        await harness.Poll();
        harness.Processes.Running = true;
        await harness.Poll();

        harness.Held.Hash = null;
        harness.Processes.Running = false;

        Assert.Empty(await harness.Poll());
    }


    private sealed class Harness
    {
        private readonly FakeGameState _state = new();
        private readonly SavegameBindingStore _bindings;
        private readonly GameRunningMonitor _running;


        public Harness()
        {
            _state.Add(Keys.Game(), new PersistedGame
            {
                GameAdapterId = new GameAdapterId("farmingSimulator", 1),
                Name = "Farming Simulator 25",
                AdapterLocalSettings = "{}",
                Targets = []
            });

            _bindings = new SavegameBindingStore(_state);
            Held = new Readings(_bindings);
            _running = new GameRunningMonitor(new Candidates(), new ProcessNames(), Processes);

            Watch = new PlaySessionWatch(
                new Candidates(),
                _bindings,
                Held,
                _running,
                NullLogger<PlaySessionWatch>.Instance);

            Watch.Played += (_, played) => Raised.Add(played);
        }


        public Guid RepoId { get; } = Guid.NewGuid();
        public Guid SavegameId { get; } = Guid.NewGuid();
        public Readings Held { get; }
        public Processes Processes { get; } = new();
        public PlaySessionWatch Watch { get; }
        public List<IReadOnlyList<PlayedSavegame>> Raised { get; } = [];


        /// <summary>The monitor first, then the watch - the order the app polls them in.</summary>
        public Task<IReadOnlyList<PlayedSavegame>> Poll()
        {
            _running.Poll();

            return Watch.PollAsync(CancellationToken.None);
        }

        public void Hold(string contentHash = CheckedOut) => _bindings.SetBinding(Keys.Game(), new SavegameCheckoutBinding(
            RepoId,
            SavegameId,
            Keys.Slot("savegame1"),
            1,
            contentHash,
            DateTime.UtcNow));
    }

    private sealed class Candidates : IDriftCandidateSource
    {
        public IReadOnlyList<DriftCandidate> GetDriftCandidates() => [new DriftCandidate(Keys.Game(), "FS25", [], null)];
    }

    private sealed class ProcessNames : IGameProcessNames
    {
        public IReadOnlyList<string> Get(GameIdentity game) => ["FarmingSimulator2025Game"];
    }

    private sealed class Processes : IGameProcesses
    {
        public bool Running { get; set; }

        public bool IsAnyRunning(IReadOnlyList<string> processNames) => Running;
    }

    /// <summary>Every held slot, reading as <see cref="Hash"/> - which starts out as what was checked out.</summary>
    private sealed class Readings(SavegameBindingStore bindings) : IHeldSlotReader
    {
        public string? Hash { get; set; } = CheckedOut;

        public Task<IReadOnlyList<HeldSlotReading>> ReadAsync(GameIdentity game, CancellationToken ct)
            => ReadAsync(game, bindings.GetBindings(game), ct);

        public Task<IReadOnlyList<HeldSlotReading>> ReadAsync(
            GameIdentity game, IReadOnlyList<SavegameCheckoutBinding> held, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HeldSlotReading>>([.. held.Select(x => new HeldSlotReading(x, Hash, "My farm"))]);
    }
}
