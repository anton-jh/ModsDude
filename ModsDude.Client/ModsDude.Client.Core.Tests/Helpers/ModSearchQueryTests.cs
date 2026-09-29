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
        new("multiplayer", ["mp"], ["yes", "no"])
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
