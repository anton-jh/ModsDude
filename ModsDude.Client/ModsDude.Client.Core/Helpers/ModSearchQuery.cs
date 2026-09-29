using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// What somebody typed into a mod search box, read once rather than once per row: plain words, which
/// <see cref="FuzzySearch"/> matches against a row's text, and <c>key:value</c> filters, which match
/// its <see cref="ModAttribute"/>s, and <c>key&gt;value</c> comparisons, which match them too.
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
/// <b>Comparisons.</b> <c>key&gt;5</c>, <c>key&lt;5</c>, <c>key&gt;=5</c> and <c>key&lt;=5</c> keep a
/// mod carrying a value that compares that way, in <see cref="ModAttributeOrder"/>: as numbers where
/// the value typed is one - and then only against values that are - and otherwise in natural order,
/// so <c>brand&lt;d</c> is every brand before D. There is no colon form; <c>key:&gt;5</c> is a
/// substring filter for "&gt;5". <c>key&gt;</c> with nothing after it is <c>key:</c>, so a comparison
/// being typed does not empty the list on its way.
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
    /// first colon, <c>&lt;</c> or <c>&gt;</c> - one inside a quoted value belongs to the value.
    /// </summary>
    public static AttributeFilter? TryReadFilter(string raw, IReadOnlyList<ModAttributeDefinition> attributes)
    {
        var negated = raw.StartsWith('-');
        var body = negated ? raw[1..] : raw;

        if (SplitFilter(body) is not { } split)
        {
            return null;
        }

        var (name, op, text) = split;

        if (attributes.FirstOrDefault(x => x.IsNamed(name)) is not ModAttributeDefinition definition)
        {
            return null;
        }

        var value = Unquote(text);

        return new AttributeFilter(definition.Key, value.Length == 0 ? null : value, negated, op);
    }

    /// <summary>
    /// A token's key, operator and whatever follows them, whether or not the key is one anybody
    /// declared - null where nothing comes before the operator, or a quote does.
    /// </summary>
    /// <param name="body">The token without its leading <c>-</c>.</param>
    public static (string Key, AttributeOperator Operator, string Value)? SplitFilter(string body)
    {
        var at = body.IndexOfAny([':', '<', '>']);
        var quote = body.IndexOf('"');

        if (at <= 0 || (quote >= 0 && quote < at))
        {
            return null;
        }

        var orEqual = at + 1 < body.Length && body[at + 1] == '=';

        var (op, length) = body[at] switch
        {
            '<' when orEqual => (AttributeOperator.LessOrEqual, 2),
            '<' => (AttributeOperator.Less, 1),
            '>' when orEqual => (AttributeOperator.GreaterOrEqual, 2),
            '>' => (AttributeOperator.Greater, 1),
            _ => (AttributeOperator.Contains, 1)
        };

        return (body[..at], op, body[(at + length)..]);
    }

    /// <summary>How an operator is typed.</summary>
    public static string Spell(AttributeOperator op) => op switch
    {
        AttributeOperator.Less => "<",
        AttributeOperator.LessOrEqual => "<=",
        AttributeOperator.Greater => ">",
        AttributeOperator.GreaterOrEqual => ">=",
        _ => ":"
    };

    private static string Unquote(string raw) => raw.Replace("\"", string.Empty).Trim();


    /// <summary>One whitespace-separated piece of a search, quotes and all.</summary>
    /// <param name="Start">Where it starts in the text.</param>
    public readonly record struct SearchToken(int Start, string Raw)
    {
        public int End => Start + Raw.Length;
    }

    /// <param name="Key">The declared key, never an alias.</param>
    /// <param name="Value">
    /// What the value must contain or compare with, or null for "has this key at all" whatever the operator.
    /// </param>
    /// <param name="Negated">Whether a mod matching it is the one thrown out.</param>
    public sealed record AttributeFilter(string Key, string? Value, bool Negated, AttributeOperator Operator = AttributeOperator.Contains)
    {
        public bool Matches(IReadOnlyList<ModAttribute> attributes)
        {
            var found = false;

            foreach (var attribute in attributes)
            {
                if (string.Equals(attribute.Key, Key, StringComparison.OrdinalIgnoreCase)
                    && (Value is null || Holds(attribute.Value)))
                {
                    found = true;
                    break;
                }
            }

            return found != Negated;
        }

        private bool Holds(string? value)
        {
            if (value is null)
            {
                return false;
            }

            if (Operator is AttributeOperator.Contains)
            {
                return value.Contains(Value!, StringComparison.OrdinalIgnoreCase);
            }

            // A number typed is a question about numbers: "count>5" is not answered by a count of "many".
            if (ModAttributeOrder.ReadNumber(Value!) is not null && ModAttributeOrder.ReadNumber(value) is null)
            {
                return false;
            }

            var order = ModAttributeOrder.CompareValues(value, Value!);

            return Operator switch
            {
                AttributeOperator.Less => order < 0,
                AttributeOperator.LessOrEqual => order <= 0,
                AttributeOperator.Greater => order > 0,
                _ => order >= 0
            };
        }
    }

    /// <summary>What a filter's value is to a mod's: contained in it, or compared with it.</summary>
    public enum AttributeOperator
    {
        /// <summary><c>key:value</c>.</summary>
        Contains,

        /// <summary><c>key&lt;value</c>.</summary>
        Less,

        /// <summary><c>key&lt;=value</c>.</summary>
        LessOrEqual,

        /// <summary><c>key&gt;value</c>.</summary>
        Greater,

        /// <summary><c>key&gt;=value</c>.</summary>
        GreaterOrEqual
    }
}
