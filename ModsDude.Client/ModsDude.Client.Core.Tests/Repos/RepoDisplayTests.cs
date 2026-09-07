using ModsDude.Client.Core.Repos;

namespace ModsDude.Client.Core.Tests.Repos;

public class RepoDisplayTests
{
    private static readonly Guid _a = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid _b = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid _c = new("00000000-0000-0000-0000-00000000000c");


    [Fact]
    public void Nothing_is_ambiguous_when_every_name_is_different()
    {
        var ambiguous = RepoDisplay.FindAmbiguous([
            (_a, "Vanilla"),
            (_b, "Modded")
        ]);

        Assert.Empty(ambiguous);
    }

    [Fact]
    public void Both_of_two_repos_sharing_a_name_are_ambiguous()
    {
        var ambiguous = RepoDisplay.FindAmbiguous([
            (_a, "Vanilla"),
            (_b, "Vanilla"),
            (_c, "Modded")
        ]);

        Assert.Equal([_a, _b], ambiguous.Order());
    }

    [Fact]
    public void A_name_that_differs_only_in_case_is_still_the_same_name_to_a_reader()
    {
        var ambiguous = RepoDisplay.FindAmbiguous([
            (_a, "Vanilla"),
            (_b, "vanilla")
        ]);

        Assert.Equal(2, ambiguous.Count);
    }

    [Fact]
    public void All_three_of_three_repos_sharing_a_name_are_ambiguous()
    {
        // No original and no duplicates: a tag on two of the three would be read as "these two are
        // the odd ones out", which is exactly the wrong thing to say.
        var ambiguous = RepoDisplay.FindAmbiguous([
            (_a, "Vanilla"),
            (_b, "Vanilla"),
            (_c, "Vanilla")
        ]);

        Assert.Equal(3, ambiguous.Count);
    }

    [Fact]
    public void An_empty_list_has_nothing_to_disambiguate()
    {
        Assert.Empty(RepoDisplay.FindAmbiguous([]));
    }
}
