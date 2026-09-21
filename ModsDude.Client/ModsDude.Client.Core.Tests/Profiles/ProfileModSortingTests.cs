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
            Key("middle", _now.AddDays(-5)),
            Key("newest", _now),
            Key("oldest", _now.AddDays(-30))
        };

        Assert.Equal(["oldest", "middle", "newest"], Sorted(ProfileModSort.DateAdded, true, keys));
        Assert.Equal(["newest", "middle", "oldest"], Sorted(ProfileModSort.DateAdded, false, keys));
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
            Key("b", _now),
            Key("a", _now),
            Key("older", _now.AddDays(-1))
        };

        Assert.Equal(["older", "a", "b"], Sorted(ProfileModSort.DateAdded, true, keys));
        Assert.Equal(["a", "b", "older"], Sorted(ProfileModSort.DateAdded, false, keys));
    }

    [Fact]
    public void A_missing_date_counts_as_the_newest()
    {
        var keys = new[] { Key("dated", _now.AddDays(-2)), Key("undated") };

        Assert.Equal(["undated", "dated"], Sorted(ProfileModSort.DateAdded, false, keys));
        Assert.Equal(["dated", "undated"], Sorted(ProfileModSort.DateAdded, true, keys));
    }

    [Fact]
    public void Dates_compare_as_instants_whatever_their_kind()
    {
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.Equal(0, ProfileModSorting.Compare(ProfileModSort.DateAdded, true, Key("x", utc), Key("x", local)));
    }

    [Fact]
    public void Names_open_ascending_and_dates_open_newest_first()
    {
        Assert.True(ProfileModSorting.DefaultAscending(ProfileModSort.Name));
        Assert.False(ProfileModSorting.DefaultAscending(ProfileModSort.DateAdded));
    }

    [Fact]
    public void The_name_sort_has_no_caption()
    {
        Assert.Null(ProfileModSorting.Caption(ProfileModSort.Name, Key("a", _now), _now));
    }

    [Fact]
    public void The_date_sort_captions_the_date_it_sorts_by()
    {
        Assert.StartsWith("Added ", ProfileModSorting.Caption(ProfileModSort.DateAdded, Key("a", _now.AddDays(-3)), _now));
    }

    [Fact]
    public void The_year_is_only_named_when_it_is_not_this_one()
    {
        var thisYear = Key("a", new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        var lastYear = Key("a", new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain("2026", ProfileModSorting.Caption(ProfileModSort.DateAdded, thisYear, _now));
        Assert.Contains("2025", ProfileModSorting.Caption(ProfileModSort.DateAdded, lastYear, _now));
    }


    private static ProfileModSortKey Key(string name, DateTime? added = null) => new(name, added);

    private static IEnumerable<string> Sorted(ProfileModSort sort, bool ascending, params ProfileModSortKey[] keys)
        => keys
            .Order(Comparer<ProfileModSortKey>.Create((left, right) => ProfileModSorting.Compare(sort, ascending, left, right)))
            .Select(x => x.Name);
}
