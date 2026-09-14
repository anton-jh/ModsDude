using ModsDude.Client.Core.Concurrency;

namespace ModsDude.Client.Core.Tests.Concurrency;

public class ResourceLeasesTests
{
    private const string _a = "target:a";
    private const string _b = "target:b";


    [Fact]
    public void An_exclusive_claim_on_a_free_resource_is_granted()
    {
        var leases = new ResourceLeases();

        using var lease = leases.TryAcquireExclusive(_a, "applying");

        Assert.NotNull(lease);
        Assert.True(leases.IsHeld(_a));
        Assert.Equal("applying", leases.DescribeHolder(_a));
    }

    [Fact]
    public void A_second_exclusive_claim_is_refused_rather_than_queued()
    {
        var leases = new ResourceLeases();

        using var first = leases.TryAcquireExclusive(_a, "applying");

        Assert.Null(leases.TryAcquireExclusive(_a, "re-applying"));
    }

    /// <summary>
    /// What makes the refusal usable: the button greys out, and the sentence beside it can say what is
    /// already running rather than only that something is.
    /// </summary>
    [Fact]
    public void A_refused_claim_can_name_what_holds_the_resource()
    {
        var leases = new ResourceLeases();

        using var first = leases.TryAcquireExclusive(_a, "Applying 'Vanilla' to 'BeamNG'");

        Assert.Null(leases.TryAcquireExclusive(_a, "second"));
        Assert.Equal("Applying 'Vanilla' to 'BeamNG'", leases.DescribeHolder(_a));
    }

    [Fact]
    public void Releasing_lets_the_next_claim_in()
    {
        var leases = new ResourceLeases();

        var first = leases.TryAcquireExclusive(_a, "applying");
        Assert.NotNull(first);

        first.Dispose();

        using var second = leases.TryAcquireExclusive(_a, "re-applying");

        Assert.NotNull(second);
        Assert.True(second.IsExclusive);
    }

    /// <summary>
    /// The handle is disposed by a <c>using</c> that may sit inside another one, exactly as the
    /// background task strip's is. A double release that let two applies into one mod folder would be
    /// the precise bug leases exist to stop.
    /// </summary>
    [Fact]
    public void Disposing_a_lease_twice_releases_it_once()
    {
        var leases = new ResourceLeases();

        var first = leases.TryAcquireExclusive(_a, "applying");
        Assert.NotNull(first);

        first.Dispose();

        using var second = leases.TryAcquireExclusive(_a, "re-applying");
        Assert.NotNull(second);

        // The stale handle going out of scope must not hand the resource away from its new holder.
        first.Dispose();

        Assert.True(leases.IsHeld(_a));
        Assert.Equal("re-applying", leases.DescribeHolder(_a));
        Assert.Null(leases.TryAcquireExclusive(_a, "third"));
    }

    [Fact]
    public void Different_resources_do_not_block_each_other()
    {
        var leases = new ResourceLeases();

        using var first = leases.TryAcquireExclusive(_a, "applying to a");
        using var second = leases.TryAcquireExclusive(_b, "applying to b");

        Assert.NotNull(first);
        Assert.NotNull(second);
    }


    /// <summary>
    /// A game reaching three mod folders is one gesture over three resources. Holding two of them
    /// while something else holds the third is a gesture that can neither run nor be described.
    /// </summary>
    [Fact]
    public void A_multi_key_claim_takes_all_of_them_or_none()
    {
        var leases = new ResourceLeases();

        using var blocker = leases.TryAcquireExclusive(_b, "sweeping b");

        Assert.Null(leases.TryAcquireExclusive([_a, _b], "applying to both"));

        // The half that was available must not have been quietly taken on the way to refusing.
        using var other = leases.TryAcquireExclusive(_a, "applying to a");
        Assert.NotNull(other);
    }

    [Fact]
    public void A_multi_key_claim_releases_every_key_at_once()
    {
        var leases = new ResourceLeases();

        var both = leases.TryAcquireExclusive([_a, _b], "applying to both");
        Assert.NotNull(both);

        Assert.True(leases.IsHeld(_a));
        Assert.True(leases.IsHeld(_b));

        both.Dispose();

        Assert.False(leases.IsHeld(_a));
        Assert.False(leases.IsHeld(_b));
    }

    /// <summary>
    /// Two of a game's targets can point at one folder - a dedicated server and a client sharing it -
    /// so one gesture can legitimately name the same key twice. Taking it twice would block on itself.
    /// </summary>
    [Fact]
    public void A_claim_naming_one_key_twice_does_not_block_on_itself()
    {
        var leases = new ResourceLeases();

        using var lease = leases.TryAcquireExclusive([_a, _a], "applying");

        Assert.NotNull(lease);
    }

    [Fact]
    public void A_claim_on_no_keys_is_granted_and_holds_nothing()
    {
        var leases = new ResourceLeases();

        using var lease = leases.TryAcquireExclusive([], "a game that reaches no folder");

        Assert.NotNull(lease);
        Assert.Empty(leases.DescribeAll());
    }


    /// <summary>
    /// The store's shape: installing from it and ingesting into it are safe beside each other however
    /// many syncs are running, because every path in is verified and written under its own hash.
    /// </summary>
    [Fact]
    public async Task Shared_claims_run_alongside_each_other()
    {
        var leases = new ResourceLeases();

        using var first = await leases.AcquireSharedAsync([_a], "syncing", CancellationToken.None);
        using var second = await leases.AcquireSharedAsync([_a], "syncing too", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.False(first.IsExclusive);
    }

    [Fact]
    public async Task An_exclusive_claim_is_refused_while_anything_is_reading()
    {
        var leases = new ResourceLeases();

        using var shared = await leases.AcquireSharedAsync([_a], "syncing", CancellationToken.None);

        Assert.Null(leases.TryAcquireExclusive(_a, "sweeping"));
    }

    [Fact]
    public async Task An_exclusive_claim_waits_for_the_readers_to_finish()
    {
        var leases = new ResourceLeases();

        var shared = await leases.AcquireSharedAsync([_a], "syncing", CancellationToken.None);

        var exclusive = leases.AcquireExclusiveAsync(_a, "sweeping", CancellationToken.None);

        Assert.False(exclusive.IsCompleted);

        shared.Dispose();

        using var granted = await exclusive;

        Assert.True(granted.IsExclusive);
    }

    [Fact]
    public async Task A_reader_waits_for_the_exclusive_holder_to_finish()
    {
        var leases = new ResourceLeases();

        var exclusive = await leases.AcquireExclusiveAsync(_a, "sweeping", CancellationToken.None);

        var shared = leases.AcquireSharedAsync([_a], "syncing", CancellationToken.None);

        Assert.False(shared.IsCompleted);

        exclusive.Dispose();

        using var granted = await shared;

        Assert.False(granted.IsExclusive);
    }

    /// <summary>
    /// The starvation both sides would otherwise suffer. A reader arriving after a sweep has queued
    /// must not walk past it, or a busy disk is never swept; and the queue is drained in arrival order,
    /// so the sweep does not stall applies that were already waiting either.
    /// </summary>
    [Fact]
    public async Task A_reader_arriving_after_a_waiting_sweep_does_not_jump_it()
    {
        var leases = new ResourceLeases();

        var firstReader = await leases.AcquireSharedAsync([_a], "syncing", CancellationToken.None);

        var sweep = leases.AcquireExclusiveAsync(_a, "sweeping", CancellationToken.None);
        var lateReader = leases.AcquireSharedAsync([_a], "syncing later", CancellationToken.None);

        Assert.False(sweep.IsCompleted);
        Assert.False(lateReader.IsCompleted);

        firstReader.Dispose();

        // The sweep was queued first, so it goes first - and the late reader is still waiting on it.
        using var swept = await sweep;

        Assert.False(lateReader.IsCompleted);

        swept.Dispose();

        using var late = await lateReader;

        Assert.NotNull(late);
    }

    [Fact]
    public async Task Giving_up_on_a_wait_leaves_the_queue_able_to_drain()
    {
        var leases = new ResourceLeases();

        var held = await leases.AcquireExclusiveAsync(_a, "sweeping", CancellationToken.None);

        using var cancellation = new CancellationTokenSource();

        var abandoned = leases.AcquireSharedAsync([_a], "syncing", cancellation.Token);
        var patient = leases.AcquireSharedAsync([_a], "syncing patiently", CancellationToken.None);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        held.Dispose();

        using var granted = await patient;

        Assert.NotNull(granted);
    }

    /// <summary>
    /// A cancellation that loses the race for the resource must not leave the lease held by nobody -
    /// which is the one failure mode that cannot be recovered from without restarting the app.
    /// </summary>
    [Fact]
    public async Task A_wait_cancelled_as_it_is_granted_does_not_leak_the_resource()
    {
        var leases = new ResourceLeases();

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var held = await leases.AcquireExclusiveAsync(_a, "holder", CancellationToken.None);

            using var cancellation = new CancellationTokenSource();

            var contender = leases.AcquireExclusiveAsync(_a, "contender", cancellation.Token);

            // Released and cancelled at the same moment: exactly one of the two must win.
            var release = Task.Run(held.Dispose);
            var cancel = Task.Run(cancellation.Cancel);

            await Task.WhenAll(release, cancel);

            try
            {
                (await contender).Dispose();
            }
            catch (OperationCanceledException)
            {
                // The other outcome, and equally correct.
            }

            Assert.False(leases.IsHeld(_a), $"The resource was left held on attempt {attempt}.");
        }
    }


    [Fact]
    public void Everything_held_can_be_named_at_once()
    {
        var leases = new ResourceLeases();

        using var first = leases.TryAcquireExclusive(_a, "Applying 'Vanilla' to 'BeamNG'");
        using var second = leases.TryAcquireExclusive(_b, "Importing 3 mods into 'Shared'");

        Assert.Equal(
            ["Applying 'Vanilla' to 'BeamNG'", "Importing 3 mods into 'Shared'"],
            leases.DescribeAll().Order());
    }

    [Fact]
    public void Nothing_is_remembered_about_a_resource_once_it_is_released()
    {
        var leases = new ResourceLeases();

        leases.TryAcquireExclusive(_a, "applying")?.Dispose();

        Assert.Empty(leases.DescribeAll());
        Assert.Null(leases.DescribeHolder(_a));
        Assert.False(leases.IsHeld(_a));
    }

    [Fact]
    public void Claiming_and_releasing_both_report_a_change()
    {
        var leases = new ResourceLeases();
        var changes = 0;

        leases.Changed += (_, _) => changes++;

        leases.TryAcquireExclusive(_a, "applying")?.Dispose();

        Assert.Equal(2, changes);
    }
}
