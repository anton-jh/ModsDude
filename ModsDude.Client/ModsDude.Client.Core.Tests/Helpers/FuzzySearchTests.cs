using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

/// <summary>
/// What the one search box in the client will and will not match.
/// </summary>
/// <remarks>
/// The refusals are the half worth pinning down. A fuzzy search that matches everything is a list,
/// and the two guards that stop it becoming one - a minimum length before subsequences are allowed,
/// and a bounded span for the ones that are - have no visible effect until somebody removes them.
/// </remarks>
public class FuzzySearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_box_matches_everything(string? term)
    {
        Assert.True(FuzzySearch.Matches(term, "Anything at all"));
    }

    [Fact]
    public void A_substring_matches_wherever_it_is()
    {
        Assert.True(FuzzySearch.Matches("deere", "John Deere 6R"));
        Assert.True(FuzzySearch.Matches("JOHN", "John Deere 6R"));
    }

    /// <summary>The whole point: people type the letters they remember, in order.</summary>
    [Theory]
    [InlineData("mdrn", "Modern Farming Pack")]
    [InlineData("jdeere", "John Deere 6R")]
    [InlineData("fs25rm", "FS25 Real Mod")]
    public void An_abbreviation_matches_as_a_subsequence(string term, string candidate)
    {
        Assert.True(FuzzySearch.Matches(term, candidate));
    }

    [Fact]
    public void Order_still_matters()
    {
        Assert.False(FuzzySearch.Matches("nrdm", "Modern Farming Pack"));
    }

    /// <summary>
    /// Two characters as a subsequence match almost any name, so a short term has to be a substring.
    /// </summary>
    [Fact]
    public void A_very_short_term_is_not_treated_as_an_abbreviation()
    {
        Assert.False(FuzzySearch.Matches("mp", "Modern Farming Pack"));
        Assert.True(FuzzySearch.Matches("mo", "Modern Farming Pack"));
    }

    /// <summary>
    /// Three letters forty characters apart is not an abbreviation of anything, and allowing it would
    /// make the filter show the whole list for most of what anybody types.
    /// </summary>
    [Fact]
    public void A_subsequence_spread_too_far_apart_is_not_a_match()
    {
        Assert.False(FuzzySearch.Matches("abc", "Alpha tractor pack, brilliant edition, collector's cut"));
    }

    /// <summary>
    /// Every space-separated term has to land, which is what lets somebody narrow a list by adding a
    /// word rather than by retyping.
    /// </summary>
    [Fact]
    public void Every_word_typed_has_to_match_something()
    {
        Assert.True(FuzzySearch.Matches("deere 6r", "John Deere 6R"));
        Assert.False(FuzzySearch.Matches("deere 8r", "John Deere 6R"));
    }

    /// <summary>
    /// A term may match any one field, but no term may span two of them - a hit made out of half a
    /// name and half an author is one nobody could see the reason for.
    /// </summary>
    [Fact]
    public void Terms_match_across_fields_but_never_across_the_join_between_them()
    {
        Assert.True(FuzzySearch.Matches("deere anton", "John Deere 6R", "fs25_johndeere", "Anton"));
        Assert.False(FuzzySearch.Matches("6ranton", "John Deere 6R", "fs25_johndeere", "Anton"));
    }

    [Fact]
    public void A_field_that_is_not_there_is_simply_not_matched()
    {
        Assert.True(FuzzySearch.Matches("deere", "John Deere 6R", null));
        Assert.False(FuzzySearch.Matches("zzz", null, null));
    }
}
