namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// Pulls mod names and ids out of a block of text somebody pasted - a forum post, a modpack
/// manifest, a message from whoever runs the server.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately forgiving about shape and deliberately strict about matching. What arrives here is
/// prose that happens to contain a list, so the parser strips the decoration a human list carries -
/// bullets, numbering, quotes, trailing commas - and hands back the bare terms. Deciding which mod a
/// term names is not this type's job: that is a lookup against the catalog, and it is exact, because
/// a fuzzy match that silently picks the wrong mod is worse than one the user is told was not found.
/// </para>
/// <para>
/// Order is preserved and duplicates are dropped, so a list pasted twice reads the same as a list
/// pasted once - and the count reported back ("52 names") is a count of distinct things asked for.
/// </para>
/// </remarks>
public static class ModListPaste
{
    private static readonly char[] _separators = ['\n', '\r', ',', ';', '\t'];

    /// <summary>Characters a human list puts around or in front of an entry, and means nothing by.</summary>
    private static readonly char[] _decoration = ['-', '*', '\u2022', '\u00b7', '#', '"', '\'', '[', ']', '(', ')', ' ', '.'];


    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var line in text.Split(_separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var term = Clean(line);

            if (term.Length > 0 && seen.Add(term))
            {
                result.Add(term);
            }
        }

        return result;
    }

    /// <summary>
    /// Strips the decoration, then any leading numbering.
    /// </summary>
    /// <remarks>
    /// The numbering has to be told apart from a version number that begins a name, and the space is
    /// what does it: "1. Foo" is the first item in a list, "1.0 Overhaul" is a mod. Requiring
    /// whitespace after the separator gets that right in both directions, where looking only for
    /// digits and a dot would rename the mod to "0 Overhaul".
    /// </remarks>
    private static string Clean(string line)
    {
        var term = line.Trim().Trim(_decoration);

        var digits = 0;

        while (digits < term.Length && char.IsAsciiDigit(term[digits]))
        {
            digits++;
        }

        if (digits > 0
            && digits + 1 < term.Length
            && term[digits] is '.' or ')' or ':'
            && char.IsWhiteSpace(term[digits + 1]))
        {
            var rest = term[(digits + 1)..].Trim(_decoration);

            if (rest.Length > 0)
            {
                return rest;
            }
        }

        return term;
    }
}
