using ModsDude.Client.Core.Models;
using System.Globalization;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// How attribute values are ordered: what a list sorted by one follows, and what <c>key&gt;value</c>
/// in a search compares against.
/// </summary>
/// <remarks>
/// <para>
/// <b>A ranked key's values in the adapter's order.</b> Where the definition is
/// <see cref="ModAttributeDefinition.Ranked"/>, its values come in the order it lists them, and any
/// value it does not list comes after all of those, in the order below.
/// </para>
/// <para>
/// <b>Otherwise numbers first, as numbers; then everything else in <see cref="NaturalOrder"/>.</b> A
/// value that reads as a number is compared as one, so <c>1.5</c> comes after <c>1.10</c>, which a
/// natural sort would put the other way round. Putting all of them ahead of the text is what keeps it
/// a total order a list can be sorted by - one comparison that switched between the two per pair
/// could not be.
/// </para>
/// <para>
/// <b>A mod carrying a key several times sorts by the first of them.</b> A pack spanning
/// <c>tractorsM</c> and <c>trailers</c> is found under the first in both directions rather than
/// jumping from one end to the other when the list is reversed. A mod not carrying the key at all
/// sorts after every one that does, whichever way the list runs - they are what the sort has nothing
/// to say about, and the top of the list is where the answer to it goes.
/// </para>
/// </remarks>
public static class ModAttributeOrder
{
    public static int CompareValues(ModAttributeDefinition key, string left, string right)
    {
        return (key.RankOf(left), key.RankOf(right)) switch
        {
            (int a, int b) => a.CompareTo(b),
            (int, null) => -1,
            (null, int) => 1,
            _ => CompareUnranked(left, right)
        };
    }

    /// <summary>The value if it reads as a finite number, the way anybody would type one.</summary>
    public static double? ReadNumber(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;

    /// <summary>
    /// What a mod sorts by under <paramref name="key"/>: its first value, an empty string for a key
    /// it carries with no value, and null where it does not carry the key.
    /// </summary>
    public static string? SortValue(IReadOnlyList<ModAttribute> attributes, ModAttributeDefinition key)
    {
        string? first = null;

        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, key.Key, StringComparison.OrdinalIgnoreCase))
            {
                var value = attribute.Value ?? string.Empty;

                if (first is null || CompareValues(key, value, first) < 0)
                {
                    first = value;
                }
            }
        }

        return first;
    }

    /// <summary>
    /// Two mods by <paramref name="key"/>, with a mod not carrying it last in both directions. Ties -
    /// including two mods both without it - are 0, for the caller to break.
    /// </summary>
    public static int Compare(ModAttributeDefinition key, bool ascending, IReadOnlyList<ModAttribute> left, IReadOnlyList<ModAttribute> right)
    {
        var leftValue = SortValue(left, key);
        var rightValue = SortValue(right, key);

        return (leftValue, rightValue) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => ascending ? CompareValues(key, a!, b!) : CompareValues(key, b!, a!)
        };
    }

    /// <summary>A mod's non-empty values for <paramref name="key"/>, in order, once each.</summary>
    public static IReadOnlyList<string> Values(IReadOnlyList<ModAttribute> attributes, ModAttributeDefinition key)
        => [.. attributes
            .Where(x => string.Equals(x.Key, key.Key, StringComparison.OrdinalIgnoreCase) && x.Value is { Length: > 0 })
            .Select(x => x.Value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(Comparer<string>.Create((left, right) => CompareValues(key, left, right)))];


    private static int CompareUnranked(string left, string right)
    {
        return (ReadNumber(left), ReadNumber(right)) switch
        {
            (double a, double b) => a.CompareTo(b),
            (double, null) => -1,
            (null, double) => 1,
            _ => NaturalOrder.Compare(left, right)
        };
    }
}
