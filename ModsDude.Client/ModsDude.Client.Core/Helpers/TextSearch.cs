namespace ModsDude.Client.Core.Helpers;

public readonly record struct TextMatch(int Start, int Length);

public enum TextSegmentKind
{
    Plain,
    Match,
    CurrentMatch
}

public sealed record TextSegment(string Text, TextSegmentKind Kind);

/// <summary>
/// Find-in-text as in a browser: case-insensitive, non-overlapping, in reading order.
/// </summary>
public static class TextSearch
{
    public static IReadOnlyList<TextMatch> FindMatches(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var matches = new List<TextMatch>();
        var start = 0;

        while (start <= text.Length - query.Length)
        {
            var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                break;
            }

            matches.Add(new TextMatch(index, query.Length));
            start = index + query.Length;
        }

        return matches;
    }

    /// <summary>Splits <paramref name="text"/> at the matches, marking the one at <paramref name="currentIndex"/>.</summary>
    public static IReadOnlyList<TextSegment> Segment(string text, IReadOnlyList<TextMatch> matches, int? currentIndex)
    {
        var segments = new List<TextSegment>();
        var position = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];

            if (match.Start > position)
            {
                segments.Add(new TextSegment(text[position..match.Start], TextSegmentKind.Plain));
            }

            var kind = i == currentIndex ? TextSegmentKind.CurrentMatch : TextSegmentKind.Match;
            segments.Add(new TextSegment(text.Substring(match.Start, match.Length), kind));

            position = match.Start + match.Length;
        }

        if (position < text.Length)
        {
            segments.Add(new TextSegment(text[position..], TextSegmentKind.Plain));
        }

        return segments;
    }
}
