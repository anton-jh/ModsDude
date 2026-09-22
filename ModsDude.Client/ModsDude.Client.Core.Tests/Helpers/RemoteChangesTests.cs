using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

public class RemoteChangesTests
{
    [Fact]
    public void A_handful_is_described_in_full()
    {
        var changes = new RemoteChanges(["One.", "Two."]);

        Assert.Equal($"One.{Environment.NewLine}Two.", changes.Describe());
    }

    [Fact]
    public void Past_a_handful_the_rest_is_a_count_and_the_whole_stays_within_it()
    {
        var changes = new RemoteChanges([.. Enumerable.Range(1, 8).Select(x => $"Change {x}.")]);

        var lines = changes.Describe().Split(Environment.NewLine);

        Assert.Equal(["Change 1.", "Change 2.", "Change 3.", "Change 4.", "And 4 more."], lines);
    }
}
