using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Helpers;

public class ModSearchCompleterTests
{
    private static readonly IReadOnlyList<ModAttributeDefinition> _declared =
    [
        new("category", ["cat"], ["tractorsS", "tractorsM", "frontLoaderTools", "wheelLoaderTools"]),
        new("brand", [], []),
        new("kind", [], ["vehicle", "script"]),
        new("multiplayer", ["mp"], ["yes", "no"])
    ];


    [Fact]
    public void An_empty_box_offers_every_key()
    {
        var completion = Complete("", 0);

        Assert.Equal(["brand", "category", "kind", "multiplayer"], completion.Items.Select(x => x.Text));
        Assert.All(completion.Items, x => Assert.True(x.IsKey));
    }

    [Theory]
    [InlineData("ca", "category")]
    [InlineData("mp", "multiplayer")]
    [InlineData("pla", "multiplayer")]
    public void Typing_narrows_the_keys_by_name_or_alias(string typed, string expected)
    {
        Assert.Equal([expected], Complete(typed, typed.Length).Items.Select(x => x.Text));
    }

    [Fact]
    public void A_key_is_inserted_with_its_colon_and_keeps_a_negation()
    {
        var item = Assert.Single(Complete("-ki", 3).Items);

        Assert.Equal("-kind:", item.Insert);
    }

    [Fact]
    public void After_the_colon_the_values_are_offered_prefix_matches_first()
    {
        var completion = Complete("category:loader", 15);

        Assert.Equal(["frontLoaderTools", "wheelLoaderTools"], completion.Items.Select(x => x.Text));

        completion = Complete("category:tr", 11);

        Assert.Equal(["tractorsM", "tractorsS"], completion.Items.Select(x => x.Text));
        Assert.Equal("category:tractorsM", completion.Items[0].Insert);
    }

    [Fact]
    public void An_alias_stays_as_typed_when_a_value_is_chosen()
    {
        var item = Complete("-cat:tractorsS", 14).Items[0];

        Assert.Equal("-cat:tractorsS", item.Insert);
    }

    [Fact]
    public void The_catalog_adds_values_the_game_did_not_declare()
    {
        var completer = new ModSearchCompleter(_declared);
        completer.SetCatalogValues([new("brand", "new holland"), new("brand", "claas"), new("category", "myOwnCategory"), new("undeclared", "x")]);

        Assert.Equal(["claas", "new holland"], completer.Complete("brand:", 6)!.Items.Select(x => x.Text));
        Assert.Contains("myOwnCategory", completer.Complete("category:my", 11)!.Items.Select(x => x.Text));
    }

    [Fact]
    public void A_value_with_a_space_is_quoted()
    {
        var completer = new ModSearchCompleter(_declared);
        completer.SetCatalogValues([new("brand", "new holland")]);

        Assert.Equal("brand:\"new holland\"", completer.Complete("brand:\"new h", 12)!.Items.Single().Insert);
    }

    /// <summary>
    /// The choice replaces the token the caret is in, all of it, so accepting with the caret in the
    /// middle of a word does not leave the rest of it behind - and leaves the other words alone.
    /// </summary>
    [Fact]
    public void The_whole_token_under_the_caret_is_replaced()
    {
        var completion = Complete("deere kind:veh fendt", 13);

        Assert.Equal(6, completion.ReplaceStart);
        Assert.Equal("kind:veh".Length, completion.ReplaceLength);
    }

    [Fact]
    public void Between_words_a_new_key_is_offered_at_the_caret()
    {
        var completion = Complete("deere  fendt", 6);

        Assert.Equal(6, completion.ReplaceStart);
        Assert.Equal(0, completion.ReplaceLength);
    }

    [Theory]
    [InlineData("FS25:thing", 10)]
    [InlineData("zzz", 3)]
    [InlineData("category:zzz", 12)]
    [InlineData("\"quoted", 7)]
    public void Nothing_to_offer_is_null(string text, int caret)
    {
        Assert.Null(new ModSearchCompleter(_declared).Complete(text, caret));
    }


    private static ModSearchCompletion Complete(string text, int caret)
        => new ModSearchCompleter(_declared).Complete(text, caret) ?? throw new Xunit.Sdk.XunitException("Expected a completion.");
}
