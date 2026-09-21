using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.ModVersions;

public class ModVersionIndexTests
{
    [Fact]
    public void A_version_the_comparer_places_after_the_newest_is_not_uncompared()
    {
        var set = Build(Registered("1.0", 0), Registered("1.1", 1), LocalOnly("1.2"));

        Assert.False(set.CouldNotCompareToNewest(V("1.2")));
    }

    [Fact]
    public void A_version_the_comparer_places_before_the_newest_is_not_uncompared()
    {
        var set = Build(Registered("1.0", 0), Registered("1.1", 1), LocalOnly("1.0-beta"));

        Assert.False(set.CouldNotCompareToNewest(V("1.0-beta")));
    }

    /// <summary>
    /// This is the exact case an import sends to the arbitration dialog, and it is the one worth
    /// naming on the row: nothing settled whether it comes before or after what the repo holds.
    /// </summary>
    [Fact]
    public void A_version_the_comparer_abstains_on_against_the_newest_is_uncompared()
    {
        var set = Build(Registered("1.0", 0), LocalOnly("2024.03"));

        Assert.True(set.CouldNotCompareToNewest(V("2024.03")));
    }

    [Fact]
    public void A_version_that_is_not_a_number_at_all_is_uncompared_and_still_in_the_order()
    {
        var set = Build(Registered("1.0", 0), LocalOnly("hurr durr"));

        Assert.True(set.CouldNotCompareToNewest(V("hurr durr")));
        Assert.Equal([V("1.0"), V("hurr durr")], set.Order.Select(x => x.VersionId));
    }

    [Fact]
    public void The_newest_registered_version_is_never_uncompared_against_itself()
    {
        var set = Build(Registered("1.0", 0), Registered("1.1", 1));

        Assert.False(set.CouldNotCompareToNewest(V("1.1")));
    }

    [Fact]
    public void A_mod_the_repo_holds_nothing_of_has_nothing_to_be_uncompared_against()
    {
        var set = Build(LocalOnly("1.0"), LocalOnly("2024.03"));

        Assert.False(set.CouldNotCompareToNewest(V("2024.03")));
    }

    [Fact]
    public void A_version_unknown_to_the_set_is_not_uncompared()
    {
        var set = Build(Registered("1.0", 0));

        Assert.False(set.CouldNotCompareToNewest(V("9.9")));
    }

    /// <summary>
    /// An abstention another version settles transitively is not a question, so it is not uncompared
    /// either - the same rule <see cref="ModVersionPartialOrderTests"/> covers for the ordering
    /// itself.
    /// </summary>
    [Fact]
    public void An_abstention_another_version_settles_transitively_is_not_uncompared()
    {
        var set = Build(Registered("1.0", 0), LocalOnly("1.1"), LocalOnly("1.2"));

        // 1.0 < 1.1 < 1.2 is fully settled by the default comparer, so nothing here is a question.
        Assert.False(set.CouldNotCompareToNewest(V("1.1")));
        Assert.False(set.CouldNotCompareToNewest(V("1.2")));
    }


    private static ModVersionSet Build(params CatalogModVersion[] versions)
    {
        var index = ModVersionIndex.Build(versions, DefaultModVersionComparer.Instance);

        return Assert.Single(index.Values);
    }

    private static CatalogModVersion Registered(string version, int sequenceNumber)
        => new(Mod("map"), V(version), "map", "", IsLocal: false, IsOnServer: true, Locked: false)
        {
            SequenceNumber = sequenceNumber
        };

    private static CatalogModVersion LocalOnly(string version)
        => new(Mod("map"), V(version), "map", "", IsLocal: true, IsOnServer: false, Locked: false);
}
