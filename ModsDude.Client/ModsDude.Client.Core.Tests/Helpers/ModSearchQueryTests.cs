using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Helpers;

public class ModSearchQueryTests
{
    private static readonly IReadOnlyList<ModAttributeDefinition> _declared =
    [
        new("category", ["cat"], ["tractorsS", "tractorsM", "silos"]),
        new("brand", [], []),
        new("kind", [], ["vehicle", "script"]),
        new("multiplayer", ["mp"], ["yes", "no"]),
        new("seats", [], [])
    ];

    private static readonly IReadOnlyList<ModAttribute> _tractor =
    [
        new("category", "tractorsM"),
        new("brand", "new holland"),
        new("kind", "vehicle"),
        new("multiplayer", "yes")
    ];

    private static readonly IReadOnlyList<ModAttribute> _script =
    [
        new("kind", "script"),
        new("multiplayer", "no")
    ];


    [Theory]
    [InlineData("category:tractorsM", true)]
    [InlineData("category:tractors", true)]
    [InlineData("CATEGORY:TRACTORSM", true)]
    [InlineData("category:silos", false)]
    [InlineData("kind:vehicle multiplayer:yes", true)]
    [InlineData("kind:vehicle multiplayer:no", false)]
    public void A_filter_keeps_a_mod_carrying_a_matching_value(string search, bool expected)
    {
        Assert.Equal(expected, Matches(search, _tractor));
    }

    [Theory]
    [InlineData("cat:tractors")]
    [InlineData("mp:yes")]
    [InlineData("MP:yes")]
    public void An_alias_filters_on_the_key_it_stands_for(string search)
    {
        Assert.True(Matches(search, _tractor));
        Assert.Equal(search.Split(':')[0].ToLowerInvariant() == "cat" ? "category" : "multiplayer",
            Assert.Single(ModSearchQuery.Parse(search, _declared).Filters).Key);
    }

    [Fact]
    public void A_key_with_no_value_keeps_any_mod_carrying_it()
    {
        Assert.True(Matches("category:", _tractor));
        Assert.False(Matches("category:", _script));
    }

    [Theory]
    [InlineData("-kind:script", true, false)]
    [InlineData("-category:", false, true)]
    [InlineData("-mp:no", true, false)]
    public void A_leading_dash_throws_the_matches_out(string search, bool tractor, bool script)
    {
        Assert.Equal(tractor, Matches(search, _tractor));
        Assert.Equal(script, Matches(search, _script));
    }

    [Fact]
    public void A_quoted_value_keeps_its_space()
    {
        Assert.True(Matches("brand:\"new holland\"", _tractor));
        Assert.False(Matches("brand:\"new zealand\"", _tractor));
    }

    /// <summary>
    /// Half of ModHub names itself <c>FS25: Something</c>. A key the game never declared is text,
    /// or those mods could not be searched for by name.
    /// </summary>
    [Fact]
    public void A_key_nobody_declared_is_a_plain_word()
    {
        var query = ModSearchQuery.Parse("FS25:tractor", _declared);

        Assert.Empty(query.Filters);
        Assert.Equal(["FS25:tractor"], query.Words);
        Assert.True(query.Matches([], "FS25:tractor pack"));
    }

    [Theory]
    [InlineData("seats>2", true)]
    [InlineData("seats>4", false)]
    [InlineData("seats>=4", true)]
    [InlineData("seats<10", true)]
    [InlineData("seats<4", false)]
    [InlineData("seats<=4", true)]
    [InlineData("seats>3.5", true)]
    public void A_comparison_with_a_number_compares_numbers(string search, bool expected)
    {
        // As text, "4" is after "10"; as a number it is not.
        Assert.Equal(expected, Matches(search, [new("seats", "4")]));
    }

    [Fact]
    public void A_comparison_with_a_number_skips_values_that_are_not_numbers()
    {
        Assert.False(Matches("seats>2", [new("seats", "many")]));
        Assert.True(Matches("seats>2", [new("seats", "many"), new("seats", "3")]));
    }

    [Theory]
    [InlineData("brand<d", true)]
    [InlineData("brand>d", false)]
    [InlineData("brand>=CLAAS", true)]
    [InlineData("brand<claas", false)]
    [InlineData("brand<\"deutz fahr\"", true)]
    public void A_comparison_with_text_compares_in_natural_order_ignoring_case(string search, bool expected)
    {
        Assert.Equal(expected, Matches(search, [new("brand", "claas")]));
    }

    [Fact]
    public void A_comparison_reads_its_operator_and_resolves_an_alias()
    {
        var filter = Assert.Single(ModSearchQuery.Parse("-cat>=silos", _declared).Filters);

        Assert.Equal(new ModSearchQuery.AttributeFilter("category", "silos", true, ModSearchQuery.AttributeOperator.GreaterOrEqual), filter);
    }

    /// <summary>A comparison half typed keeps the list as it would be for <c>key:</c>, not empty.</summary>
    [Fact]
    public void A_comparison_with_nothing_after_it_keeps_any_mod_carrying_the_key()
    {
        Assert.True(Matches("category>", _tractor));
        Assert.False(Matches("category>", _script));
    }

    /// <summary>The operators take no colon. <c>key:&gt;5</c> looks for "&gt;5" in the value.</summary>
    [Fact]
    public void A_colon_before_the_operator_makes_it_part_of_the_value()
    {
        var filter = Assert.Single(ModSearchQuery.Parse("seats:>2", _declared).Filters);

        Assert.Equal(ModSearchQuery.AttributeOperator.Contains, filter.Operator);
        Assert.Equal(">2", filter.Value);
        Assert.False(Matches("seats:>2", [new("seats", "4")]));
    }

    [Fact]
    public void A_comparison_on_a_key_nobody_declared_is_a_plain_word()
    {
        var query = ModSearchQuery.Parse("x>5", _declared);

        Assert.Empty(query.Filters);
        Assert.Equal(["x>5"], query.Words);
    }

    [Fact]
    public void Words_and_filters_must_all_hold()
    {
        var query = ModSearchQuery.Parse("deere kind:vehicle", _declared);

        Assert.True(query.Matches(_tractor, "John Deere 6R"));
        Assert.False(query.Matches(_tractor, "Fendt 700"));
        Assert.False(query.Matches(_script, "John Deere 6R"));
    }

    [Fact]
    public void Words_are_matched_fuzzily_as_before()
    {
        Assert.True(ModSearchQuery.Parse("johndeere", _declared).Matches([], "John Deere 6R"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_search_matches_everything(string? search)
    {
        var query = ModSearchQuery.Parse(search, _declared);

        Assert.True(query.IsEmpty);
        Assert.True(query.Matches([], "anything"));
    }

    [Fact]
    public void A_game_with_no_attributes_reads_everything_as_words()
    {
        var query = ModSearchQuery.Parse("category:silos", []);

        Assert.Empty(query.Filters);
        Assert.Equal(["category:silos"], query.Words);
    }

    [Fact]
    public void Tokens_split_on_whitespace_but_not_inside_quotes()
    {
        var tokens = ModSearchQuery.Tokenize("deere  brand:\"new holland\" -kind:script");

        Assert.Equal(["deere", "brand:\"new holland\"", "-kind:script"], tokens.Select(x => x.Raw));
        Assert.Equal([0, 7, 27], tokens.Select(x => x.Start));
    }

    /// <summary>
    /// Mid-typing, the value being completed is one token to the end, not two halves - otherwise the
    /// completion list would replace only the part after the space.
    /// </summary>
    [Fact]
    public void An_unclosed_quote_runs_to_the_end()
    {
        var tokens = ModSearchQuery.Tokenize("x brand:\"new ho");

        Assert.Equal(["x", "brand:\"new ho"], tokens.Select(x => x.Raw));
    }


    private static bool Matches(string search, IReadOnlyList<ModAttribute> attributes)
        => ModSearchQuery.Parse(search, _declared).Matches(attributes, "Some mod");
}
