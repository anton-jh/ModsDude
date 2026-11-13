using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

public class RequestIdKeeperTests
{
    private sealed record Request(string Name, int Level);


    [Fact]
    public void The_same_request_sent_again_keeps_its_id()
    {
        var keeper = new RequestIdKeeper<Request>();

        var first = keeper.For(new Request("Friends", 1));
        var again = keeper.For(new Request("Friends", 1));

        Assert.Equal(first, again);
    }

    [Fact]
    public void A_changed_request_gets_a_new_id()
    {
        var keeper = new RequestIdKeeper<Request>();

        var first = keeper.For(new Request("Friends", 1));
        var changed = keeper.For(new Request("Friends", 2));

        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void Going_back_to_an_earlier_request_after_a_different_one_gets_a_new_id()
    {
        var keeper = new RequestIdKeeper<Request>();

        var first = keeper.For(new Request("Friends", 1));
        keeper.For(new Request("Friends", 2));
        var back = keeper.For(new Request("Friends", 1));

        Assert.NotEqual(first, back);
    }

    [Fact]
    public void After_forgetting_the_same_request_is_a_new_one()
    {
        var keeper = new RequestIdKeeper<Request>();

        var first = keeper.For(new Request("Friends", 1));
        keeper.Forget();
        var next = keeper.For(new Request("Friends", 1));

        Assert.NotEqual(first, next);
    }

    [Fact]
    public void Ids_are_random_rather_than_derived_from_the_request()
    {
        var one = new RequestIdKeeper<Request>().For(new Request("Friends", 1));
        var other = new RequestIdKeeper<Request>().For(new Request("Friends", 1));

        Assert.NotEqual(one, other);
    }

    [Fact]
    public void A_comparer_decides_what_counts_as_the_same_request()
    {
        var keeper = new RequestIdKeeper<Request>(ReferenceEqualityComparer.Instance);
        var request = new Request("Friends", 1);

        var first = keeper.For(request);

        Assert.Equal(first, keeper.For(request));
        Assert.NotEqual(first, keeper.For(new Request("Friends", 1)));
    }
}
