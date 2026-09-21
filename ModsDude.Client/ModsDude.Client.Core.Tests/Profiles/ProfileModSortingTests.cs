using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModSortingTests
{
    private static readonly DateTime _now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void Names_run_a_to_z_and_can_be_reversed()
    {
        var keys = new[] { Key("Mod 10"), Key("Mod 9"), Key("alpha") };

        Assert.Equal(["alpha", "Mod 9", "Mod 10"], Sorted(ProfileModSort.Name, true, keys));
        Assert.Equal(["Mod 10", "Mod 9", "alpha"], Sorted(ProfileModSort.Name, false, keys));
    }

    [Fact]
    public void Dates_run_oldest_first_ascending_and_newest_first_descending()
    {
        var keys = new[]
        {
            Key("middle", added: _now.AddDays(-5)),
            Key("newest", added: _now),
            Key("oldest", added: _now.AddDays(-30))
        };

        Assert.Equal(["oldest", "middle", "newest"], Sorted(ProfileModSort.DateAdded, true, keys));
        Assert.Equal(["newest", "middle", "oldest"], Sorted(ProfileModSort.DateAdded, false, keys));
    }

    [Fact]
    public void Each_sort_reads_its_own_date()
    {
        var keys = new[]
        {
            Key("a", added: _now.AddDays(-1), registered: _now.AddDays(-30)),
            Key("b", added: _now.AddDays(-10), registered: _now.AddDays(-5))
        };

        Assert.Equal(["a", "b"], Sorted(ProfileModSort.DateAdded, false, keys));
        Assert.Equal(["b", "a"], Sorted(ProfileModSort.Registered, false, keys));
    }

    /// <summary>
    /// A mod nothing has registered yet is the most recent thing in the list, so newest-first puts it at
    /// the top with the rest of what the draft just did.
    /// </summary>
    [Fact]
    public void A_missing_date_counts_as_the_newest()
    {
        var keys = new[]
        {
            Key("registered", registered: _now.AddDays(-2)),
            Key("unregistered")
        };

        Assert.Equal(["unregistered", "registered"], Sorted(ProfileModSort.Registered, false, keys));
        Assert.Equal(["registered", "unregistered"], Sorted(ProfileModSort.Registered, true, keys));
    }

    /// <summary>
    /// Ties are broken by name in the same direction whichever way the dates run - otherwise reversing
    /// would shuffle the mods a single save added together, rather than reverse the list.
    /// </summary>
    [Fact]
    public void Mods_with_the_same_date_stay_in_name_order_in_both_directions()
    {
        var keys = new[]
        {
            Key("b", added: _now),
            Key("a", added: _now),
            Key("older", added: _now.AddDays(-1))
        };

        Assert.Equal(["older", "a", "b"], Sorted(ProfileModSort.DateAdded, true, keys));
        Assert.Equal(["a", "b", "older"], Sorted(ProfileModSort.DateAdded, false, keys));
    }

    [Fact]
    public void Dates_compare_as_instants_whatever_their_kind()
    {
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.Equal(0, ProfileModSorting.Compare(
            ProfileModSort.DateAdded, true, Key("x", added: utc), Key("x", added: local)));
    }

    [Fact]
    public void Names_open_ascending_and_dates_open_newest_first()
    {
        Assert.True(ProfileModSorting.DefaultAscending(ProfileModSort.Name));
        Assert.False(ProfileModSorting.DefaultAscending(ProfileModSort.DateAdded));
        Assert.False(ProfileModSorting.DefaultAscending(ProfileModSort.Registered));
    }

    [Fact]
    public void The_name_sort_has_no_caption()
    {
        Assert.Null(ProfileModSorting.Caption(ProfileModSort.Name, Key("a", added: _now), _now));
    }

    [Fact]
    public void A_date_sort_captions_the_date_it_sorts_by()
    {
        var key = Key("a", added: _now.AddDays(-3), registered: _now.AddDays(-9));

        Assert.StartsWith("Added ", ProfileModSorting.Caption(ProfileModSort.DateAdded, key, _now));
        Assert.StartsWith("Registered ", ProfileModSorting.Caption(ProfileModSort.Registered, key, _now));
    }

    [Fact]
    public void The_year_is_only_named_when_it_is_not_this_one()
    {
        var thisYear = Key("a", added: new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        var lastYear = Key("a", added: new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain("2026", ProfileModSorting.Caption(ProfileModSort.DateAdded, thisYear, _now));
        Assert.Contains("2025", ProfileModSorting.Caption(ProfileModSort.DateAdded, lastYear, _now));
    }

    [Fact]
    public void A_mod_the_repo_has_not_registered_says_so_under_the_registered_sort()
    {
        Assert.Equal("Not registered", ProfileModSorting.Caption(ProfileModSort.Registered, Key("a", added: _now), _now));
    }


    private static ProfileModSortKey Key(string name, DateTime? added = null, DateTime? registered = null)
        => new(name, added, registered);

    private static IEnumerable<string> Sorted(ProfileModSort sort, bool ascending, params ProfileModSortKey[] keys)
        => keys
            .Order(Comparer<ProfileModSortKey>.Create((left, right) => ProfileModSorting.Compare(sort, ascending, left, right)))
            .Select(x => x.Name);
}
