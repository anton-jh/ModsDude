using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Helpers;

public class ModAttributeOrderTests
{
    private static readonly ModAttributeDefinition _brand = new("brand", [], []);
    private static readonly ModAttributeDefinition _category = new("category", [], ["trailers", "tractorsM"]);
    private static readonly ModAttributeDefinition _kind = new("kind", [], ["map", "script", "vehicle", "placeable"], Ranked: true);


    [Fact]
    public void Numbers_compare_as_numbers_and_come_before_text()
    {
        string[] values = ["b", "1.10", "10", "1.5", "A", "2"];

        Assert.Equal(["1.10", "1.5", "2", "10", "A", "b"], values.Order(Comparer<string>.Create((x, y) => ModAttributeOrder.CompareValues(_brand, x, y))));
    }

    [Fact]
    public void An_unranked_key_ignores_the_order_its_values_are_listed_in()
    {
        Assert.True(ModAttributeOrder.CompareValues(_category, "tractorsM", "trailers") < 0);
    }

    [Fact]
    public void A_ranked_key_follows_its_listed_order_ignoring_case_with_unlisted_values_after()
    {
        string[] values = ["zz", "Placeable", "10", "vehicle", "aa", "SCRIPT", "map"];

        Assert.Equal(
            ["map", "SCRIPT", "vehicle", "Placeable", "10", "aa", "zz"],
            values.Order(Comparer<string>.Create((x, y) => ModAttributeOrder.CompareValues(_kind, x, y))));
    }

    [Fact]
    public void A_mod_sorts_by_its_first_value_for_the_key()
    {
        IReadOnlyList<ModAttribute> pack = [new("category", "trailers"), new("category", "tractorsM"), new("brand", "aaa")];

        Assert.Equal("tractorsM", ModAttributeOrder.SortValue(pack, _category with { Key = "CATEGORY" }));
        Assert.Null(ModAttributeOrder.SortValue(pack, _kind));
        Assert.Equal(string.Empty, ModAttributeOrder.SortValue([new("multiplayer", null)], new("multiplayer", [], [])));
    }

    [Fact]
    public void A_script_that_ships_store_items_sorts_as_a_script()
    {
        IReadOnlyList<ModAttribute> precisionFarming = [new("kind", "placeable"), new("kind", "script"), new("kind", "vehicle")];
        IReadOnlyList<ModAttribute> tractor = [new("kind", "vehicle")];

        Assert.Equal("script", ModAttributeOrder.SortValue(precisionFarming, _kind));
        Assert.True(ModAttributeOrder.Compare(_kind, true, precisionFarming, tractor) < 0);
        Assert.True(ModAttributeOrder.Compare(_kind, false, precisionFarming, tractor) > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mod_without_the_key_sorts_last_both_ways(bool ascending)
    {
        IReadOnlyList<ModAttribute> tagged = [new("brand", "claas")];
        IReadOnlyList<ModAttribute> untagged = [new("kind", "vehicle")];

        Assert.True(ModAttributeOrder.Compare(_brand, ascending, tagged, untagged) < 0);
        Assert.True(ModAttributeOrder.Compare(_brand, ascending, untagged, tagged) > 0);
        Assert.Equal(0, ModAttributeOrder.Compare(_brand, ascending, untagged, untagged));
    }

    [Fact]
    public void Descending_reverses_the_values()
    {
        IReadOnlyList<ModAttribute> claas = [new("brand", "claas")];
        IReadOnlyList<ModAttribute> fendt = [new("brand", "fendt")];

        Assert.True(ModAttributeOrder.Compare(_brand, true, claas, fendt) < 0);
        Assert.True(ModAttributeOrder.Compare(_brand, false, claas, fendt) > 0);
    }

    [Fact]
    public void The_values_come_in_order_once_each()
    {
        IReadOnlyList<ModAttribute> pack = [new("category", "trailers"), new("category", "tractorsM"), new("category", "TRAILERS")];

        Assert.Equal(["tractorsM", "trailers"], ModAttributeOrder.Values(pack, _category));
        Assert.Empty(ModAttributeOrder.Values(pack, _brand));
        Assert.Equal(["script", "vehicle"], ModAttributeOrder.Values([new("kind", "vehicle"), new("kind", "script")], _kind));
    }
}
