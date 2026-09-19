using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

/// <summary>
/// The guess at how long is left: quiet until it has watched long enough, honest about lumpy or
/// stalled work, and restarted whenever the distance changes.
/// </summary>
public class RemainingTimeEstimatorTests
{
    private readonly TestClock _clock = new();
    private readonly RemainingTimeEstimator _estimator;

    public RemainingTimeEstimatorTests()
    {
        _estimator = new RemainingTimeEstimator(_clock);
    }


    [Fact]
    public void Nothing_is_offered_before_it_has_watched_for_a_while()
    {
        _estimator.Observe("Fetching", 0, 100);
        _clock.Advance(TimeSpan.FromSeconds(2));
        _estimator.Observe("Fetching", 10, 100);

        Assert.Null(_estimator.Remaining);
    }

    [Fact]
    public void A_steady_rate_gives_the_time_the_rest_will_take()
    {
        Run("Fetching", total: 100, seconds: 10, perSecond: 2);

        // 20 of 100 done at two a second: 80 left is forty seconds.
        var remaining = _estimator.Remaining;

        Assert.NotNull(remaining);
        Assert.Equal(40, remaining.Value.TotalSeconds, precision: 0);
    }

    [Fact]
    public void Nothing_is_offered_without_any_movement()
    {
        Run("Fetching", total: 100, seconds: 10, perSecond: 0);

        Assert.Null(_estimator.Remaining);
    }

    [Fact]
    public void An_uncountable_total_clears_the_estimate()
    {
        Run("Fetching", total: 100, seconds: 10, perSecond: 2);

        _estimator.Observe("Fetching", 0, 0);

        Assert.Null(_estimator.Remaining);
    }

    [Fact]
    public void A_new_stage_starts_over_rather_than_inheriting_the_last_rate()
    {
        Run("Packing", total: 100, seconds: 10, perSecond: 10);

        _estimator.Observe("Uploading", 0, 500);

        Assert.Null(_estimator.Remaining);
    }

    [Fact]
    public void A_count_that_goes_backwards_starts_over()
    {
        Run("Fetching", total: 100, seconds: 10, perSecond: 2);

        _estimator.Observe("Fetching", 1, 100);

        Assert.Null(_estimator.Remaining);
    }

    [Fact]
    public void Work_that_goes_quiet_stretches_its_estimate_instead_of_freezing_it()
    {
        Run("Fetching", total: 100, seconds: 10, perSecond: 2);
        var before = _estimator.Remaining!.Value;

        _clock.Advance(TimeSpan.FromSeconds(20));

        Assert.True(_estimator.Remaining is null || _estimator.Remaining.Value > before);
    }

    [Fact]
    public void The_rate_follows_a_change_of_pace_once_the_old_one_leaves_the_window()
    {
        Run("Fetching", total: 10_000, seconds: 30, perSecond: 100);

        // Then ten times slower for longer than the window.
        for (var i = 0; i < 40; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _estimator.Observe("Fetching", 3_000 + (i + 1) * 10, 10_000);
        }

        var remaining = _estimator.Remaining;

        Assert.NotNull(remaining);

        // 3,400 done, 6,600 left at ten a second is 660 s; the old rate would have said 66 s.
        Assert.InRange(remaining.Value.TotalSeconds, 600, 720);
    }

    [Theory]
    [InlineData(3, "a few seconds left")]
    [InlineData(20, "about 20 s left")]
    [InlineData(23, "about 25 s left")]
    [InlineData(58, "about 1 min left")]
    [InlineData(61, "about 2 min left")]
    [InlineData(600, "about 10 min left")]
    [InlineData(3600, "about 1 h left")]
    [InlineData(3600 + 25 * 60, "about 1 h 25 min left")]
    public void It_is_described_coarsely(int seconds, string expected)
    {
        Assert.Equal(expected, RemainingTimeEstimator.Describe(TimeSpan.FromSeconds(seconds)));
    }


    /// <summary>Reports <paramref name="perSecond"/> more of the work each second, for that long.</summary>
    private void Run(string stage, long total, int seconds, long perSecond)
    {
        _estimator.Observe(stage, 0, total);

        for (var i = 1; i <= seconds; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _estimator.Observe(stage, i * perSecond, total);
        }
    }


    private sealed class TestClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
