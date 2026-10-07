using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Stores;

namespace ModsDude.Client.Core.Tests.Stores;

public class StoreLoadsTests
{
    [Fact]
    public async Task Callers_arriving_while_a_read_is_out_share_it()
    {
        var harness = new Harness();
        var gate = harness.Server.Hold();

        var first = harness.ReadAsync();
        var second = harness.ReadAsync();

        gate.SetResult(7);
        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.Server.Reads);
        Assert.Equal([7], harness.Applied);
    }

    [Fact]
    public async Task A_read_after_the_last_one_finished_asks_again()
    {
        var harness = new Harness();

        await harness.ReadAsync();
        await harness.ReadAsync();

        Assert.Equal(2, harness.Server.Reads);
    }

    [Fact]
    public async Task A_read_that_crossed_a_write_is_read_again_instead_of_applied()
    {
        var harness = new Harness();
        var stale = harness.Server.Hold();

        var read = harness.ReadAsync();
        await harness.Loads.WriteAsync(default, _ => Task.FromResult(2), harness.Applied.Add, CancellationToken.None);

        stale.SetResult(1);
        await read;

        Assert.Equal(2, harness.Server.Reads);
        Assert.Equal([2, Harness.Fresh], harness.Applied);
    }

    [Fact]
    public async Task A_read_that_keeps_crossing_writes_gives_up_without_applying_anything_stale()
    {
        var harness = new Harness();
        harness.Server.OnRead = () => harness.Loads.ApplyAsync(default, () => { });

        await harness.ReadAsync();

        Assert.Empty(harness.Applied);
        Assert.Equal(5, harness.Server.Reads);
    }

    [Fact]
    public async Task A_user_change_cancels_the_read_and_drops_its_answer()
    {
        var harness = new Harness();
        var gate = harness.Server.Hold();

        var read = harness.ReadAsync();
        harness.Loads.Reset();
        gate.SetResult(1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Empty(harness.Applied);
    }

    [Fact]
    public async Task A_write_answered_after_a_user_change_is_not_applied()
    {
        var harness = new Harness();
        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var write = harness.Loads.WriteAsync(default, _ => answer.Task, harness.Applied.Add, CancellationToken.None);
        harness.Loads.Reset();
        answer.SetResult(3);

        Assert.Equal(3, await write);
        Assert.Empty(harness.Applied);
    }

    [Fact]
    public async Task One_caller_giving_up_leaves_the_read_running_for_the_others()
    {
        var harness = new Harness();
        var gate = harness.Server.Hold();
        using var impatient = new CancellationTokenSource();

        var cancelled = harness.ReadAsync(impatient.Token);
        var patient = harness.ReadAsync();

        await impatient.CancelAsync();
        gate.SetResult(4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await patient;
        Assert.Equal([4], harness.Applied);
    }

    [Fact]
    public async Task A_failed_read_reaches_every_caller_and_the_next_read_starts_afresh()
    {
        var harness = new Harness();
        var gate = harness.Server.Hold();

        var first = harness.ReadAsync();
        var second = harness.ReadAsync();
        gate.SetException(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => first);
        await Assert.ThrowsAsync<HttpRequestException>(() => second);

        await harness.ReadAsync();

        Assert.Equal([Harness.Fresh], harness.Applied);
    }

    [Fact]
    public async Task Reads_of_different_keys_do_not_wait_for_each_other()
    {
        var loads = new StoreLoads<int>(InlineStoreDispatcher.Instance, NullLogger.Instance);
        var held = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<int>();

        var slow = loads.ReadAsync(1, _ => held.Task, applied.Add, CancellationToken.None);
        await loads.ReadAsync(2, _ => Task.FromResult(20), applied.Add, CancellationToken.None);

        held.SetResult(10);
        await slow;

        Assert.Equal([20, 10], applied);
    }


    private sealed class Harness
    {
        public const int Fresh = 100;

        public Harness()
        {
            Loads = new StoreLoads<WholeList>(InlineStoreDispatcher.Instance, NullLogger.Instance);
        }

        public FakeServer Server { get; } = new();
        public StoreLoads<WholeList> Loads { get; }
        public List<int> Applied { get; } = [];

        public Task ReadAsync(CancellationToken cancellationToken = default)
            => Loads.ReadAsync(default, Server.ReadAsync, Applied.Add, cancellationToken);
    }

    private sealed class FakeServer
    {
        private readonly Queue<TaskCompletionSource<int>> _held = [];

        public int Reads { get; private set; }

        public Func<Task>? OnRead { get; set; }

        /// <summary>Holds the next read until the test answers it.</summary>
        public TaskCompletionSource<int> Hold()
        {
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _held.Enqueue(gate);

            return gate;
        }

        public async Task<int> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;

            if (_held.TryDequeue(out var gate))
            {
                return await gate.Task.WaitAsync(cancellationToken);
            }

            if (OnRead is not null)
            {
                await OnRead();
            }

            return Harness.Fresh;
        }
    }
}
