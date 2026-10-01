using ModsDude.Client.Core.Builds;

namespace ModsDude.Client.Core.Tests.Builds;

public class ServerCompatibilityTests
{
    private readonly ServerCompatibility _compatibility = new(new BuildNumber(10));
    private int _changes;


    public ServerCompatibilityTests()
    {
        _compatibility.Changed += (_, _) => _changes++;
    }


    [Fact]
    public void It_starts_out_compatible()
    {
        _compatibility.ReportAccepted();

        Assert.Null(_compatibility.Mismatch);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void A_newer_server_means_this_copy_is_behind()
    {
        _compatibility.ReportRefused(new BuildNumber(11));

        Assert.Equal(new BuildMismatch(new BuildNumber(10), new BuildNumber(11)), _compatibility.Mismatch);
        Assert.True(_compatibility.Mismatch!.ClientIsBehind);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void An_acceptance_arriving_after_a_newer_server_refused_does_not_clear_it()
    {
        _compatibility.ReportRefused(new BuildNumber(11));
        _compatibility.ReportAccepted();

        Assert.NotNull(_compatibility.Mismatch);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void An_older_server_is_cleared_once_it_accepts()
    {
        _compatibility.ReportRefused(new BuildNumber(9));

        Assert.False(_compatibility.Mismatch!.ClientIsBehind);

        _compatibility.ReportAccepted();

        Assert.Null(_compatibility.Mismatch);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void The_same_refusal_twice_is_one_change()
    {
        _compatibility.ReportRefused(new BuildNumber(11));
        _compatibility.ReportRefused(new BuildNumber(11));

        Assert.Equal(1, _changes);
    }

    [Fact]
    public void A_server_moving_further_ahead_is_followed()
    {
        _compatibility.ReportRefused(new BuildNumber(11));
        _compatibility.ReportRefused(new BuildNumber(12));

        Assert.Equal(new BuildNumber(12), _compatibility.Mismatch!.Server);
    }

    [Fact]
    public void A_late_refusal_from_a_build_the_server_has_moved_past_is_ignored()
    {
        _compatibility.ReportRefused(new BuildNumber(12));
        _compatibility.ReportRefused(new BuildNumber(11));
        _compatibility.ReportRefused(new BuildNumber(9));

        Assert.Equal(new BuildNumber(12), _compatibility.Mismatch!.Server);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void A_server_that_was_behind_and_then_moved_ahead_is_followed()
    {
        _compatibility.ReportRefused(new BuildNumber(9));
        _compatibility.ReportRefused(new BuildNumber(11));

        Assert.True(_compatibility.Mismatch!.ClientIsBehind);
    }

    [Fact]
    public void A_refusal_naming_this_build_is_not_a_mismatch()
    {
        _compatibility.ReportRefused(new BuildNumber(10));

        Assert.Null(_compatibility.Mismatch);
    }

    [Fact]
    public async Task Concurrent_reports_settle_on_the_newest_server()
    {
        var reports = Enumerable.Range(0, 200)
            .Select(i => Task.Run(() =>
            {
                if (i % 2 == 0)
                {
                    _compatibility.ReportAccepted();
                }
                else
                {
                    _compatibility.ReportRefused(new BuildNumber(11 + i % 5));
                }
            }));

        await Task.WhenAll(reports);

        Assert.Equal(new BuildNumber(15), _compatibility.Mismatch!.Server);
    }
}
