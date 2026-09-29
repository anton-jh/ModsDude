using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// What a mod search box offers on Ctrl+Space: the attribute keys while a key is being typed, and
/// that key's values once it has its colon. Knows nothing about the box - it is handed the text and
/// the caret and answers with what to show and what part of the text a choice replaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Values are the game's list and the catalog's, together.</b> A key like <c>category</c> has a
/// list the game defines, which is what lets somebody find a category nothing in view carries yet;
/// a key like <c>brand</c> has none, and a category a mod invented is in no list. Both come from
/// what the catalog actually holds, which <see cref="SetCatalogValues"/> keeps current.
/// </para>
/// <para>
/// <b>Narrowed by substring, prefix matches first.</b> Typing <c>tractors</c> keeps
/// <c>tractorsS</c>, <c>tractorsM</c> and <c>tractorsL</c>; typing <c>loader</c> keeps every loader
/// category rather than only the ones that start with it, and the ones that do start with it come first.
/// </para>
/// </remarks>
public sealed class ModSearchCompleter(IReadOnlyList<ModAttributeDefinition> attributes)
{
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _values = Merge(attributes, []);


    public IReadOnlyList<ModAttributeDefinition> Attributes { get; } = attributes;


    /// <summary>Takes in every value the catalog's versions carry, replacing what it had.</summary>
    public void SetCatalogValues(IEnumerable<ModAttribute> catalogAttributes)
        => _values = Merge(Attributes, catalogAttributes);

    /// <summary>
    /// What to offer for the token the caret is in, or null where there is nothing to offer there -
    /// a key nobody declared, or a word that is not a filter at all.
    /// </summary>
    /// <param name="caret">Where the caret is, as a TextBox counts it: 0 before the first character.</param>
    public ModSearchCompletion? Complete(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);

        var token = ModSearchQuery.Tokenize(text).FirstOrDefault(x => x.Start <= caret && caret <= x.End);

        // Between two words, or in an empty box: a new token starting at the caret, with nothing typed.
        if (token.Raw is null)
        {
            token = new ModSearchQuery.SearchToken(caret, string.Empty);
        }

        var typed = token.Raw[..(caret - token.Start)];
        var negation = typed.StartsWith('-') ? "-" : string.Empty;
        var body = typed[negation.Length..];

        var colon = body.IndexOf(':');

        return colon < 0
            ? CompleteKey(token, negation, body)
            : CompleteValue(token, negation, body[..colon], body[(colon + 1)..]);
    }

    private ModSearchCompletion? CompleteKey(ModSearchQuery.SearchToken token, string negation, string typed)
    {
        // A quote before any colon makes this a quoted word, not a key being typed.
        if (typed.Contains('"'))
        {
            return null;
        }

        var items = Attributes
            .Select(x => (Definition: x, Rank: Rank(typed, [x.Key, .. x.Aliases])))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ModSearchCompletionItem(
                x.Definition.Key,
                x.Definition.Aliases.Count > 0 ? string.Join(", ", x.Definition.Aliases) : null,
                $"{negation}{x.Definition.Key}:",
                IsKey: true))
            .ToList();

        return items.Count == 0 ? null : new ModSearchCompletion(token.Start, token.Raw.Length, items);
    }

    private ModSearchCompletion? CompleteValue(ModSearchQuery.SearchToken token, string negation, string name, string typed)
    {
        if (Attributes.FirstOrDefault(x => x.IsNamed(name)) is not ModAttributeDefinition definition)
        {
            return null;
        }

        var value = typed.Replace("\"", string.Empty);

        var items = _values.GetValueOrDefault(definition.Key, [])
            .Select(x => (Value: x, Rank: Rank(value, [x])))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            // Keeps what was typed for the key - an alias stays an alias - so accepting a value does
            // not rewrite the part of the token somebody already chose.
            .Select(x => new ModSearchCompletionItem(x.Value, null, $"{negation}{name}:{Quote(x.Value)}", IsKey: false))
            .ToList();

        return items.Count == 0 ? null : new ModSearchCompletion(token.Start, token.Raw.Length, items);
    }

    /// <summary>0 for a prefix of any name, 1 for a substring of one, -1 for neither.</summary>
    private static int Rank(string typed, IEnumerable<string> names)
    {
        var rank = -1;

        foreach (var name in names)
        {
            if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            {
                rank = 1;
            }
        }

        return rank;
    }

    private static string Quote(string value)
        => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

    private static Dictionary<string, IReadOnlyList<string>> Merge(
        IReadOnlyList<ModAttributeDefinition> attributes,
        IEnumerable<ModAttribute> catalogAttributes)
    {
        var values = attributes.ToDictionary(
            x => x.Key,
            x => new HashSet<string>(x.Values, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in catalogAttributes)
        {
            if (attribute.Value is { Length: > 0 } value && values.TryGetValue(attribute.Key, out var set))
            {
                set.Add(value);
            }
        }

        return values.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)[.. x.Value],
            StringComparer.OrdinalIgnoreCase);
    }
}


/// <summary>
/// What to offer, and which part of the text a choice replaces - the whole token the caret is in,
/// not only the part before it, so accepting in the middle of a word does not leave its tail behind.
/// </summary>
public sealed record ModSearchCompletion(int ReplaceStart, int ReplaceLength, IReadOnlyList<ModSearchCompletionItem> Items);

/// <param name="Text">What the list shows.</param>
/// <param name="Hint">Shown beside it, quieter - a key's aliases.</param>
/// <param name="Insert">What replaces the token.</param>
/// <param name="IsKey">
/// A key, which is inserted with its colon and no trailing space so its values can be offered next;
/// a value ends the token.
/// </param>
public sealed record ModSearchCompletionItem(string Text, string? Hint, string Insert, bool IsKey);
