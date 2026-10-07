using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Changes;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Sync;
using System.Net.Sockets;

namespace ModsDude.Client.Core.Tests.Connectivity;

/// <summary>Probing a server that stopped answering, and reading everything again once it does.</summary>
public sealed class ServerReconnectTests : IDisposable
{
    private readonly TestTimeProvider _time = new();
    private readonly ServerConnection _connection = new();
    private readonly ConnectionRetry _retry;
    private readonly Probe _probe;
    private readonly FakeChangePoll _poll = new();
    private readonly FakeDriftMonitor _drift = new();
    private readonly ServerReconnect _reconnect;
    private readonly CancellationTokenSource _stopping = new();
    private int _reconnected;


    public ServerReconnectTests()
    {
        _retry = new ConnectionRetry(NullLogger<ConnectionRetry>.Instance, _time);
        _probe = new Probe(_connection);
        _reconnect = new ServerReconnect(_connection, _retry, _probe, _poll, _drift, _time, NullLogger<ServerReconnect>.Instance);
        _reconnect.Reconnected += (_, _) => Interlocked.Increment(ref _reconnected);
    }


    [Fact]
    public async Task Nothing_is_probed_while_the_server_answers()
    {
        var run = _reconnect.RunAsync(_stopping.Token);

        await Task.Delay(50);

        Assert.Equal(0, _probe.Count);
        Assert.False(run.IsCompleted);
    }

    [Fact]
    public async Task Going_offline_probes_until_the_server_answers_then_reads_everything_again()
    {
        var run = _reconnect.RunAsync(_stopping.Token);

        _connection.ReportUnreachable();
        await WaitUntil(() => _probe.Count == 1 && _time.PendingTimers == 1);

        Assert.Equal(1, _drift.Checks);
        Assert.Equal(0, _poll.Rereads);

        _probe.ServerIsUp = true;
        _time.Advance(ConnectionRetry.DelayAfter(1));

        await WaitUntil(() => Volatile.Read(ref _reconnected) == 1);

        Assert.True(_connection.IsOnline);
        Assert.Equal(2, _probe.Count);
        Assert.Equal(1, _poll.Rereads);
        Assert.Equal(2, _drift.Checks);
        Assert.Null(_retry.GetOutage(ConnectionTarget.Server));
        Assert.False(run.IsCompleted);
    }

    /// <summary>Anything else getting an answer - the first repo load, say - means the probe need not wait out its delay.</summary>
    [Fact]
    public async Task Another_request_getting_through_cuts_the_probes_wait_short()
    {
        _ = _reconnect.RunAsync(_stopping.Token);

        _connection.ReportUnreachable();
        await WaitUntil(() => _probe.Count == 1 && _time.PendingTimers == 1);

        _probe.ServerIsUp = true;
        _connection.ReportReachable();

        await WaitUntil(() => Volatile.Read(ref _reconnected) == 1);

        Assert.Equal(2, _probe.Count);
        Assert.Equal(1, _poll.Rereads);
    }

    [Fact]
    public async Task Going_offline_again_is_noticed_again()
    {
        _ = _reconnect.RunAsync(_stopping.Token);
        _probe.ServerIsUp = true;

        _connection.ReportUnreachable();
        await WaitUntil(() => Volatile.Read(ref _reconnected) == 1);

        _connection.ReportUnreachable();
        await WaitUntil(() => Volatile.Read(ref _reconnected) == 2);

        Assert.Equal(2, _poll.Rereads);
    }

    [Fact]
    public async Task A_failed_reread_still_counts_as_back_online()
    {
        _ = _reconnect.RunAsync(_stopping.Token);
        _probe.ServerIsUp = true;
        _poll.FailRereads = true;

        _connection.ReportUnreachable();

        await WaitUntil(() => Volatile.Read(ref _reconnected) == 1);
        Assert.True(_connection.IsOnline);
    }

    [Fact]
    public async Task Stopping_ends_the_loop_while_it_probes()
    {
        var run = _reconnect.RunAsync(_stopping.Token);

        _connection.ReportUnreachable();
        await WaitUntil(() => _probe.Count == 1 && _time.PendingTimers == 1);

        await _stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref _reconnected));
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }


    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (condition() is false)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The reconnect never got there.");
            }

            await Task.Delay(10);
        }
    }


    /// <summary>The change counters as the server answers them, reporting to the connection the way the HTTP handler does.</summary>
    private sealed class Probe(ServerConnection connection) : IChangesClient
    {
        private int _count;

        public volatile bool ServerIsUp;

        public int Count => Volatile.Read(ref _count);

        public Task<GetChangesResponse> GetChangesV1Async(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);

            if (ServerIsUp is false)
            {
                connection.ReportUnreachable();

                throw new HttpRequestException("No connection could be made.", new SocketException((int)SocketError.ConnectionRefused));
            }

            connection.ReportReachable();

            return Task.FromResult(new GetChangesResponse { Repos = [] });
        }
    }

    private sealed class FakeChangePoll : IChangePoll
    {
        private int _rereads;

        public int Rereads => Volatile.Read(ref _rereads);
        public bool FailRereads { get; set; }

        public Task PollAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RereadAllAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _rereads);

            return FailRereads ? throw new HttpRequestException("The connection dropped.") : Task.CompletedTask;
        }

        public void ClearUserState() { }
    }

    private sealed class FakeDriftMonitor : IDriftMonitor
    {
        private int _checks;

        public event EventHandler? Changed { add { } remove { } }

        public int Checks => Volatile.Read(ref _checks);

        public IReadOnlyList<TargetDrift> Drifted => [];
        public bool HasDrift => false;
        public IReadOnlyList<CorruptedBlob> StoreCorruption => [];
        public bool HasStoreCorruption => false;
        public bool HasAnything => false;

        public Task<bool> CheckAsync(DriftCheckReason reason = DriftCheckReason.Explicit)
        {
            Interlocked.Increment(ref _checks);

            return Task.FromResult(false);
        }

        public bool Check(DriftCheckReason reason = DriftCheckReason.Explicit) => throw new NotSupportedException();
        public void Watch() { }
        public void Dispose() { }
    }
}
