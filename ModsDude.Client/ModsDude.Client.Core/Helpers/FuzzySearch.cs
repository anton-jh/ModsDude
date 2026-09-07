namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// The one thing behind every search box in the client: does what somebody typed describe this row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Subsequence matching, in the fzf sense</b> - the typed characters have to appear in order, not
/// adjacently. <c>fs25</c> finds "FS25 Real Mod", <c>mdrn</c> finds "Modern Farming", and
/// <c>johndeere</c> finds "John Deere 6R". That is the whole of the fuzziness: no edit distance, no
/// transpositions, no scoring that has to be tuned. A search that quietly matches something the user
/// did not type is worse than one that matches nothing, and a mod list is a list of names people
/// abbreviate rather than misspell.
/// </para>
/// <para>
/// <b>Two guards keep it from matching everything.</b> A term shorter than
/// <see cref="_minimumFuzzyLength"/> has to appear as a substring, because two characters as a
/// subsequence match almost any name; and a longer term's matched characters have to fit inside a
/// bounded span, so <c>abc</c> does not match a name that happens to contain an a, a b and a c forty
/// characters apart.
/// </para>
/// <para>
/// <b>Whitespace splits the term into independent ones, all of which must match</b> - each against
/// any of the candidate fields. That is what makes "deere 6r" work when the name carries one half and
/// the author the other, and it is why the fields are passed in rather than concatenated: a term
/// spanning the join of two fields would be a match nobody could see the reason for.
/// </para>
/// </remarks>
public static class FuzzySearch
{
    /// <summary>
    /// Below this, a term must be a substring. One or two characters as a subsequence is not a
    /// filter, it is a list.
    /// </summary>
    private const int _minimumFuzzyLength = 3;

    private static readonly char[] _separators = [' ', '\t', '\n', '\r'];


    /// <summary>
    /// Whether <paramref name="term"/> describes any of <paramref name="candidates"/>. An empty or
    /// whitespace term matches everything, which is what an empty search box means.
    /// </summary>
    public static bool Matches(string? term, params ReadOnlySpan<string?> candidates)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        foreach (var token in term.Split(_separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (MatchesAny(token, candidates) is false)
            {
                return false;
            }
        }

        return true;
    }


    private static bool MatchesAny(string token, ReadOnlySpan<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is not null && MatchesOne(token, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One term against one field. A substring is always a match; anything else has to be a
    /// subsequence tight enough to have been an abbreviation of it.
    /// </summary>
    private static bool MatchesOne(string token, string candidate)
    {
        if (candidate.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return token.Length >= _minimumFuzzyLength && IsSubsequence(token, candidate);
    }

    /// <summary>
    /// Whether the term's characters appear in order within a bounded span of the candidate.
    /// </summary>
    /// <remarks>
    /// Every possible start is tried rather than only the first one that matches, because greedy
    /// matching from the first occurrence refuses matches that exist: <c>zoo</c> against "Zielonka
    /// Zoo" consumes the leading Z and then finds no second o in "ielonka". The candidates here are
    /// names, ids and author lines rather than paragraphs, so the quadratic worst case is a few
    /// hundred comparisons per row.
    /// </remarks>
    private static bool IsSubsequence(string token, string candidate)
    {
        // Long enough for an abbreviation of a couple of words, short enough that three letters
        // scattered across a long name is not a match.
        var maximumSpan = (token.Length * 3) + 6;

        for (var start = 0; start + token.Length <= candidate.Length; start++)
        {
            if (char.ToUpperInvariant(candidate[start]) != char.ToUpperInvariant(token[0]))
            {
                continue;
            }

            var taken = 1;
            var at = start + 1;
            var limit = Math.Min(candidate.Length, start + maximumSpan);

            while (taken < token.Length && at < limit)
            {
                if (char.ToUpperInvariant(candidate[at]) == char.ToUpperInvariant(token[taken]))
                {
                    taken++;
                }

                at++;
            }

            if (taken == token.Length)
            {
                return true;
            }
        }

        return false;
    }
}
