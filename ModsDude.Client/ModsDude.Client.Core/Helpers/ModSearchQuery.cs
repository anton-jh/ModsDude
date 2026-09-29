using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// What somebody typed into a mod search box, read once rather than once per row: plain words, which
/// <see cref="FuzzySearch"/> matches against a row's text, and <c>key:value</c> filters, which match
/// its <see cref="ModAttribute"/>s.
/// </summary>
/// <remarks>
/// <para>
/// <b>The syntax.</b> <c>category:tractors</c> keeps a mod carrying a <c>category</c> whose value
/// contains "tractors", ignoring case. <c>kind:</c> with nothing after it keeps a mod carrying any
/// <c>kind</c> at all. A leading <c>-</c> turns either around. A value with a space in it is quoted,
/// <c>brand:"new holland"</c>. Every term must hold, filters and words alike, exactly as every word
/// had to before.
/// </para>
/// <para>
/// <b>Only a declared key makes a filter.</b> <c>FS25:</c> is how half the mods on ModHub start their
/// names, and a search for one must still find it - so a token whose key the game never declared is
/// a word like any other, colon, dash and all. That is also why an alias is resolved here rather
/// than stored: <c>cat:silos</c> and <c>category:silos</c> are the same filter, and the attribute
/// only ever says <c>category</c>.
/// </para>
/// <para>
/// <b>Values match by substring, not fuzzily.</b> A filter is somebody naming a tag, usually one the
/// completion list just handed them; <c>tractors</c> finding <c>tractorsS</c>, <c>tractorsM</c> and
/// <c>tractorsL</c> is the useful kind of loose, and an abbreviation finding a category nobody meant
/// is not.
/// </para>
/// </remarks>
public sealed class ModSearchQuery
{
    public static ModSearchQuery Empty { get; } = new([], []);

    private readonly IReadOnlyList<string> _words;
    private readonly IReadOnlyList<AttributeFilter> _filters;


    private ModSearchQuery(IReadOnlyList<string> words, IReadOnlyList<AttributeFilter> filters)
    {
        _words = words;
        _filters = filters;
    }


    /// <summary>Whether this matches everything, which is what an empty search box means.</summary>
    public bool IsEmpty => _words.Count == 0 && _filters.Count == 0;

    /// <summary>The attribute filters, for tests and for anything that wants to say what is being filtered.</summary>
    public IReadOnlyList<AttributeFilter> Filters => _filters;

    /// <summary>The plain words, which are matched against a row's text.</summary>
    public IReadOnlyList<string> Words => _words;


    /// <param name="attributes">The keys the game declared. A key it did not declare is a word.</param>
    public static ModSearchQuery Parse(string? text, IReadOnlyList<ModAttributeDefinition> attributes)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var words = new List<string>();
        var filters = new List<AttributeFilter>();

        foreach (var token in Tokenize(text))
        {
            if (TryReadFilter(token.Raw, attributes) is AttributeFilter filter)
            {
                filters.Add(filter);
            }
            else if (Unquote(token.Raw) is { Length: > 0 } word)
            {
                words.Add(word);
            }
        }

        return new ModSearchQuery(words, filters);
    }

    /// <summary>
    /// Whether a row with these attributes and this text is one somebody typing this meant.
    /// </summary>
    /// <param name="candidates">The row's searchable text, field by field - see <see cref="FuzzySearch.Matches"/>.</param>
    public bool Matches(IReadOnlyList<ModAttribute> attributes, params ReadOnlySpan<string?> candidates)
    {
        foreach (var filter in _filters)
        {
            if (filter.Matches(attributes) is false)
            {
                return false;
            }
        }

        foreach (var word in _words)
        {
            if (FuzzySearch.Matches(word, candidates) is false)
            {
                return false;
            }
        }

        return true;
    }


    /// <summary>
    /// The text split the way <see cref="Parse"/> reads it: on whitespace, except inside double
    /// quotes. Each token keeps its quotes and where it sits, which is what a completion list needs
    /// to replace the one under the caret.
    /// </summary>
    /// <remarks>
    /// An unclosed quote runs to the end of the text, so a value being typed - <c>brand:"new ho</c> -
    /// is one token all the way through rather than two half-tokens.
    /// </remarks>
    public static IReadOnlyList<SearchToken> Tokenize(string text)
    {
        var tokens = new List<SearchToken>();
        var start = -1;
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c) && quoted is false)
            {
                if (start >= 0)
                {
                    tokens.Add(new SearchToken(start, text[start..i]));
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }

            if (c == '"')
            {
                quoted = quoted is false;
            }
        }

        if (start >= 0)
        {
            tokens.Add(new SearchToken(start, text[start..]));
        }

        return tokens;
    }

    /// <summary>
    /// Reads a token as a filter, or null where it is a word. The key is everything before the
    /// first colon - a colon inside a quoted value belongs to the value.
    /// </summary>
    public static AttributeFilter? TryReadFilter(string raw, IReadOnlyList<ModAttributeDefinition> attributes)
    {
        var negated = raw.StartsWith('-');
        var body = negated ? raw[1..] : raw;

        var colon = body.IndexOf(':');
        var quote = body.IndexOf('"');

        if (colon <= 0 || (quote >= 0 && quote < colon))
        {
            return null;
        }

        var name = body[..colon];

        if (attributes.FirstOrDefault(x => x.IsNamed(name)) is not ModAttributeDefinition definition)
        {
            return null;
        }

        var value = Unquote(body[(colon + 1)..]);

        return new AttributeFilter(definition.Key, value.Length == 0 ? null : value, negated);
    }

    private static string Unquote(string raw) => raw.Replace("\"", string.Empty).Trim();


    /// <summary>One whitespace-separated piece of a search, quotes and all.</summary>
    /// <param name="Start">Where it starts in the text.</param>
    public readonly record struct SearchToken(int Start, string Raw)
    {
        public int End => Start + Raw.Length;
    }

    /// <param name="Key">The declared key, never an alias.</param>
    /// <param name="Value">What the value must contain, or null for "has this key at all".</param>
    /// <param name="Negated">Whether a mod matching it is the one thrown out.</param>
    public sealed record AttributeFilter(string Key, string? Value, bool Negated)
    {
        public bool Matches(IReadOnlyList<ModAttribute> attributes)
        {
            var found = false;

            foreach (var attribute in attributes)
            {
                if (string.Equals(attribute.Key, Key, StringComparison.OrdinalIgnoreCase)
                    && (Value is null || (attribute.Value?.Contains(Value, StringComparison.OrdinalIgnoreCase) ?? false)))
                {
                    found = true;
                    break;
                }
            }

            return found != Negated;
        }
    }
}
