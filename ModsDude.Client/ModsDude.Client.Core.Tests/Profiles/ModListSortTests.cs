using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ModListSortTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly ModListDateWording _added = ModListDateWording.AddedToProfile;


    [Fact]
    public void Names_run_a_to_z_and_can_be_reversed()
    {
        var keys = new[] { Key("Mod 10"), Key("Mod 9"), Key("alpha") };

        Assert.Equal(["alpha", "Mod 9", "Mod 10"], Sorted(new ModListSort(ModListSortKind.Name), keys));
        Assert.Equal(["Mod 10", "Mod 9", "alpha"], Sorted(new ModListSort(ModListSortKind.Name, Ascending: false), keys));
    }

    [Fact]
    public void Dates_run_oldest_first_ascending_and_newest_first_descending()
    {
        var keys = new[] { Key("middle", _now.AddDays(-5)), Key("newest", _now), Key("oldest", _now.AddDays(-30)) };

        Assert.Equal(["oldest", "middle", "newest"], Sorted(new ModListSort(ModListSortKind.Date), keys));
        Assert.Equal(["newest", "middle", "oldest"], Sorted(new ModListSort(ModListSortKind.Date, Ascending: false), keys));
    }

    [Fact]
    public void Mods_with_the_same_date_stay_in_name_order_in_both_directions()
    {
        var keys = new[] { Key("b", _now), Key("a", _now), Key("older", _now.AddDays(-1)) };

        Assert.Equal(["older", "a", "b"], Sorted(new ModListSort(ModListSortKind.Date), keys));
        Assert.Equal(["a", "b", "older"], Sorted(new ModListSort(ModListSortKind.Date, Ascending: false), keys));
    }

    [Fact]
    public void A_missing_date_counts_as_the_newest()
    {
        var keys = new[] { Key("dated", _now.AddDays(-2)), Key("undated") };

        Assert.Equal(["undated", "dated"], Sorted(new ModListSort(ModListSortKind.Date, Ascending: false), keys));
        Assert.Equal(["dated", "undated"], Sorted(new ModListSort(ModListSortKind.Date), keys));
    }

    [Fact]
    public void Names_and_attributes_open_ascending_and_dates_open_newest_first()
    {
        Assert.True(ModListSort.Default(ModListSortKind.Name).Ascending);
        Assert.True(ModListSort.Default(ModListSortKind.Attribute, "brand").Ascending);
        Assert.False(ModListSort.Default(ModListSortKind.Date).Ascending);
    }

    [Fact]
    public void An_attribute_sort_orders_by_value_then_name_with_the_untagged_last()
    {
        var keys = new[]
        {
            Tagged("untagged"),
            Tagged("b fendt", ("brand", "fendt")),
            Tagged("claas", ("brand", "claas")),
            Tagged("a fendt", ("brand", "fendt"))
        };

        Assert.Equal(["claas", "a fendt", "b fendt", "untagged"], Sorted(new ModListSort(ModListSortKind.Attribute, "brand"), keys));
        Assert.Equal(["a fendt", "b fendt", "claas", "untagged"], Sorted(new ModListSort(ModListSortKind.Attribute, "brand", false), keys));
    }

    [Fact]
    public void The_attribute_sort_captions_the_values()
    {
        var key = Tagged("pack", ("category", "trailers"), ("category", "tractorsM"));
        var sort = new ModListSort(ModListSortKind.Attribute, "category");

        Assert.Equal("tractorsM, trailers", sort.Caption(key, _now, _added));
        Assert.Equal("category: tractorsM, trailers", sort.Describe(key, _added));
        Assert.Null((sort with { Attribute = "brand" }).Caption(key, _now, _added));
    }

    [Fact]
    public void The_name_sort_has_no_caption()
        => Assert.Null(new ModListSort().Caption(Key("a", _now), _now, _added));

    [Fact]
    public void The_date_sort_captions_the_date_in_the_lists_own_words()
    {
        var sort = new ModListSort(ModListSortKind.Date);

        Assert.StartsWith("Added ", sort.Caption(Key("a", _now.AddDays(-3)), _now, _added));
        Assert.StartsWith("Imported ", sort.Caption(Key("a", _now.AddDays(-3)), _now, ModListDateWording.ImportedToRepo));
        Assert.Equal("Unsaved", sort.Caption(Key("a"), _now, _added));
    }

    [Fact]
    public void The_year_is_only_named_when_it_is_not_this_one()
    {
        var sort = new ModListSort(ModListSortKind.Date);

        Assert.DoesNotContain("2026", sort.Caption(Key("a", new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)), _now, _added));
        Assert.Contains("2025", sort.Caption(Key("a", new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)), _now, _added));
    }


    private static ModListSortKey Key(string name, DateTimeOffset? date = null) => new(name, date, []);

    private static ModListSortKey Tagged(string name, params (string Key, string Value)[] attributes)
        => new(name, null, [.. attributes.Select(x => new ModAttribute(x.Key, x.Value))]);

    private static IEnumerable<string> Sorted(ModListSort sort, params ModListSortKey[] keys)
        => keys.Order(Comparer<ModListSortKey>.Create(sort.Compare)).Select(x => x.Name);
}
