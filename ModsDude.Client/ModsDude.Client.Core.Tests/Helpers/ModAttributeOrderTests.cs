using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Helpers;

public class ModAttributeOrderTests
{
    [Fact]
    public void Numbers_compare_as_numbers_and_come_before_text()
    {
        string[] values = ["b", "1.10", "10", "1.5", "A", "2"];

        Assert.Equal(["1.10", "1.5", "2", "10", "A", "b"], values.Order(Comparer<string>.Create(ModAttributeOrder.CompareValues)));
    }

    [Fact]
    public void A_mod_sorts_by_its_lowest_value_for_the_key()
    {
        IReadOnlyList<ModAttribute> pack = [new("category", "trailers"), new("category", "tractorsM"), new("brand", "aaa")];

        Assert.Equal("tractorsM", ModAttributeOrder.SortValue(pack, "CATEGORY"));
        Assert.Null(ModAttributeOrder.SortValue(pack, "kind"));
        Assert.Equal(string.Empty, ModAttributeOrder.SortValue([new("multiplayer", null)], "multiplayer"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mod_without_the_key_sorts_last_both_ways(bool ascending)
    {
        IReadOnlyList<ModAttribute> tagged = [new("brand", "claas")];
        IReadOnlyList<ModAttribute> untagged = [new("kind", "vehicle")];

        Assert.True(ModAttributeOrder.Compare("brand", ascending, tagged, untagged) < 0);
        Assert.True(ModAttributeOrder.Compare("brand", ascending, untagged, tagged) > 0);
        Assert.Equal(0, ModAttributeOrder.Compare("brand", ascending, untagged, untagged));
    }

    [Fact]
    public void Descending_reverses_the_values()
    {
        IReadOnlyList<ModAttribute> claas = [new("brand", "claas")];
        IReadOnlyList<ModAttribute> fendt = [new("brand", "fendt")];

        Assert.True(ModAttributeOrder.Compare("brand", true, claas, fendt) < 0);
        Assert.True(ModAttributeOrder.Compare("brand", false, claas, fendt) > 0);
    }

    [Fact]
    public void The_caption_lists_the_values_in_order_once_each()
    {
        IReadOnlyList<ModAttribute> pack = [new("category", "trailers"), new("category", "tractorsM"), new("category", "TRAILERS")];

        Assert.Equal("tractorsM, trailers", ModAttributeOrder.Caption(pack, "category"));
        Assert.Null(ModAttributeOrder.Caption(pack, "brand"));
    }
}
