using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Activity;

public class PresenceReporterTests
{
    private static readonly GameIdentity _game = GameIdentity.Parse("_farming_simulator#fs25");


    [Fact]
    public async Task A_running_game_on_a_profile_beats_once_a_minute()
    {
        var harness = new Harness();
        harness.GameRunning = true;

        await harness.Report();
        harness.Time.Advance(TimeSpan.FromSeconds(30));
        await harness.Report();
        harness.Time.Advance(TimeSpan.FromSeconds(30));
        await harness.Report();

        Assert.Equal([true, true], harness.Client.Sent);
    }

    [Fact]
    public async Task A_running_game_on_no_profile_says_nothing()
    {
        var harness = new Harness { HasProfile = false };
        harness.GameRunning = true;

        await harness.Report();

        Assert.Empty(harness.Client.Sent);
    }

    [Fact]
    public async Task A_closed_game_stops_once()
    {
        var harness = new Harness();
        harness.GameRunning = true;
        await harness.Report();

        harness.GameRunning = false;
        await harness.Report();
        await harness.Report();

        Assert.Equal([true, false], harness.Client.Sent);
    }

    [Fact]
    public async Task A_game_never_told_about_is_not_stopped()
    {
        var harness = new Harness();

        await harness.Report();

        Assert.Empty(harness.Client.Sent);
    }

    /// <summary>A heartbeat that did not land is not a session the server knows about yet.</summary>
    [Fact]
    public async Task A_failed_heartbeat_is_tried_again_on_the_next_call()
    {
        var harness = new Harness();
        harness.GameRunning = true;
        harness.Client.Fail = true;

        await harness.Report();

        harness.Client.Fail = false;
        await harness.Report();

        Assert.Equal([true, true], harness.Client.Sent);
    }

    /// <summary>Otherwise a game closed while the server was away would look played until it timed out.</summary>
    [Fact]
    public async Task A_failed_stop_is_tried_again_on_the_next_call()
    {
        var harness = new Harness();
        harness.GameRunning = true;
        await harness.Report();

        harness.GameRunning = false;
        harness.Client.Fail = true;
        await harness.Report();

        harness.Client.Fail = false;
        await harness.Report();
        await harness.Report();

        Assert.Equal([true, false, false], harness.Client.Sent);
    }

    [Fact]
    public async Task Nothing_is_sent_while_offline()
    {
        var harness = new Harness();
        harness.GameRunning = true;
        harness.Connection.IsOnline = false;

        await harness.Report();

        Assert.Empty(harness.Client.Sent);
    }

    [Fact]
    public async Task Cancelling_a_report_throws_and_leaves_it_unsent()
    {
        var harness = new Harness();
        harness.GameRunning = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Reporter.ReportAsync(new CancellationToken(canceled: true)));
        await harness.Report();

        Assert.Equal([true], harness.Client.Sent);
    }

    [Fact]
    public async Task Signing_out_forgets_the_sessions()
    {
        var harness = new Harness();
        harness.GameRunning = true;
        await harness.Report();

        harness.Reporter.ClearUserState();
        await harness.Report();

        Assert.Equal([true, true], harness.Client.Sent);
    }


    private sealed class Harness : IDriftCandidateSource, IGameRunningMonitor
    {
        public Harness()
        {
            Reporter = new PresenceReporter(Client, this, this, Connection, Time, NullLogger<PresenceReporter>.Instance);
        }

        public PresenceReporter Reporter { get; }
        public FakeActivityClient Client { get; } = new();
        public FakeConnection Connection { get; } = new();
        public TestTimeProvider Time { get; } = new();
        public bool GameRunning { get; set; }
        public bool HasProfile { get; init; } = true;

        public Task Report() => Reporter.ReportAsync(CancellationToken.None);

        public IReadOnlyList<DriftCandidate> GetDriftCandidates()
            => [new DriftCandidate(_game, "FS25", [], HasProfile ? new ActiveProfile(Guid.NewGuid(), Guid.NewGuid()) : null)];

        public event EventHandler? Changed { add { } remove { } }

        public bool HasPolled => true;

        public IReadOnlyList<GameIdentity> Running => GameRunning ? [_game] : [];

        public bool IsRunning(GameIdentity game) => GameRunning && game == _game;

        public void Poll() { }
    }

    private sealed class FakeActivityClient : IActivityClient
    {
        public bool Fail { get; set; }
        /// <summary>Every report tried, landed or not.</summary>
        public List<bool> Sent { get; } = [];

        public Task ReportPlayingV1Async(ReportPlayingRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Sent.Add(request.IsPlaying);

            if (Fail)
            {
                throw new HttpRequestException("Nobody answered.");
            }

            return Task.CompletedTask;
        }

        public Task<ICollection<GameActivityDto>> GetGameActivityV1Async(Guid? repoId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RecordGameActivityV1Async(RecordGameActivityRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ClearGameActivityV1Async(string game, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeConnection : IServerConnection
    {
        public bool IsOnline { get; set; } = true;

        public event EventHandler? Changed { add { } remove { } }

        public void ReportReachable() { }

        public void ReportUnreachable() { }
    }
}
