using ModsDude.Client.Core.Models;
using System.Globalization;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// How attribute values are ordered: what a list sorted by one follows, and what <c>key&gt;value</c>
/// in a search compares against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Numbers first, as numbers; then everything else in <see cref="NaturalOrder"/>.</b> A value that
/// reads as a number is compared as one, so <c>1.5</c> comes after <c>1.10</c>, which a natural sort
/// would put the other way round. Putting all of them ahead of the text is what keeps it a total
/// order a list can be sorted by - one comparison that switched between the two per pair could not be.
/// </para>
/// <para>
/// <b>A mod carrying a key several times sorts by the lowest of them.</b> A pack spanning
/// <c>tractorsM</c> and <c>trailers</c> is found under the first in both directions rather than
/// jumping from one end to the other when the list is reversed. A mod not carrying the key at all
/// sorts after every one that does, whichever way the list runs - they are what the sort has nothing
/// to say about, and the top of the list is where the answer to it goes.
/// </para>
/// </remarks>
public static class ModAttributeOrder
{
    /// <summary>Numbers ahead of text, each in their own order. See the remarks.</summary>
    public static int CompareValues(string left, string right)
    {
        var leftNumber = ReadNumber(left);
        var rightNumber = ReadNumber(right);

        return (leftNumber, rightNumber) switch
        {
            (double a, double b) => a.CompareTo(b),
            (double, null) => -1,
            (null, double) => 1,
            _ => NaturalOrder.Compare(left, right)
        };
    }

    /// <summary>The value if it reads as a finite number, the way anybody would type one.</summary>
    public static double? ReadNumber(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;

    /// <summary>
    /// What a mod sorts by under <paramref name="key"/>: its lowest value, an empty string for a key
    /// it carries with no value, and null where it does not carry the key.
    /// </summary>
    public static string? SortValue(IReadOnlyList<ModAttribute> attributes, string key)
    {
        string? lowest = null;

        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                var value = attribute.Value ?? string.Empty;

                if (lowest is null || CompareValues(value, lowest) < 0)
                {
                    lowest = value;
                }
            }
        }

        return lowest;
    }

    /// <summary>
    /// Two mods by <paramref name="key"/>, with a mod not carrying it last in both directions. Ties -
    /// including two mods both without it - are 0, for the caller to break.
    /// </summary>
    public static int Compare(string key, bool ascending, IReadOnlyList<ModAttribute> left, IReadOnlyList<ModAttribute> right)
    {
        var leftValue = SortValue(left, key);
        var rightValue = SortValue(right, key);

        return (leftValue, rightValue) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => ascending ? CompareValues(a!, b!) : CompareValues(b!, a!)
        };
    }

    /// <summary>
    /// What a row says under a sort by <paramref name="key"/>: its values in order, or null where it
    /// carries none - and so has nothing to say.
    /// </summary>
    public static string? Caption(IReadOnlyList<ModAttribute> attributes, string key)
    {
        var values = attributes
            .Where(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase) && x.Value is { Length: > 0 })
            .Select(x => x.Value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(Comparer<string>.Create(CompareValues))
            .ToList();

        return values.Count == 0 ? null : string.Join(", ", values);
    }
}
