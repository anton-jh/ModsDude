using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

public class TextSearchTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_blank_query_matches_nothing(string query)
    {
        Assert.Empty(TextSearch.FindMatches("some text with spaces", query));
    }

    [Fact]
    public void A_query_not_in_the_text_matches_nothing()
    {
        Assert.Empty(TextSearch.FindMatches("tractor", "trailer"));
    }

    [Fact]
    public void A_query_longer_than_the_text_matches_nothing()
    {
        Assert.Empty(TextSearch.FindMatches("cab", "cabin"));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        var matches = TextSearch.FindMatches("Tractor and TRACTOR", "tractor");

        Assert.Equal([new TextMatch(0, 7), new TextMatch(12, 7)], matches);
    }

    [Fact]
    public void Repeats_match_without_overlapping_in_reading_order()
    {
        var matches = TextSearch.FindMatches("aaaaa", "aa");

        Assert.Equal([new TextMatch(0, 2), new TextMatch(2, 2)], matches);
    }

    [Fact]
    public void Matches_at_the_very_start_and_end_are_found()
    {
        var matches = TextSearch.FindMatches("mod by mod", "mod");

        Assert.Equal([new TextMatch(0, 3), new TextMatch(7, 3)], matches);
    }

    [Fact]
    public void The_whole_text_can_be_one_match()
    {
        Assert.Equal([new TextMatch(0, 4)], TextSearch.FindMatches("Silo", "silo"));
    }

    [Fact]
    public void Segments_split_the_text_at_the_matches_and_mark_the_current_one()
    {
        const string text = "a cow, a Cow, a COW";
        var matches = TextSearch.FindMatches(text, "cow");

        var segments = TextSearch.Segment(text, matches, currentIndex: 1);

        Assert.Equal(
            [
                new TextSegment("a ", TextSegmentKind.Plain),
                new TextSegment("cow", TextSegmentKind.Match),
                new TextSegment(", a ", TextSegmentKind.Plain),
                new TextSegment("Cow", TextSegmentKind.CurrentMatch),
                new TextSegment(", a ", TextSegmentKind.Plain),
                new TextSegment("COW", TextSegmentKind.Match)
            ],
            segments);
    }

    [Fact]
    public void Segments_without_matches_are_the_whole_text()
    {
        Assert.Equal([new TextSegment("plain", TextSegmentKind.Plain)], TextSearch.Segment("plain", [], currentIndex: null));
    }

    [Fact]
    public void Segments_of_empty_text_are_none()
    {
        Assert.Empty(TextSearch.Segment("", [], currentIndex: null));
    }

    [Fact]
    public void Adjacent_matches_leave_no_empty_segments_between_them()
    {
        const string text = "abab";
        var matches = TextSearch.FindMatches(text, "ab");

        var segments = TextSearch.Segment(text, matches, currentIndex: 0);

        Assert.Equal(
            [
                new TextSegment("ab", TextSegmentKind.CurrentMatch),
                new TextSegment("ab", TextSegmentKind.Match)
            ],
            segments);
    }
}
