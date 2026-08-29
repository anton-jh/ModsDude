using ModsDude.Client.Core.Models;
using System.Globalization;

namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Compares the notation the overwhelming majority of mods use: dotted numerics of any depth, an
/// optional <c>v</c> prefix, zero-padded segments, and a pre-release suffix such as <c>-beta</c>,
/// <c>-rc1</c> or <c>b2</c>. Segments compare numerically, so <c>1.10</c> follows <c>1.9</c>.
/// </summary>
/// <remarks>
/// <para>
/// Where the abstention boundary sits, and why:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Numbers decide wherever both strings carry them.</b> A difference at a segment both strings
/// have is real data, and how the strings happen to be written cannot argue with it.
/// </item>
/// <item>
/// <b>How a version is written is only evidence when its numbers are not.</b> <c>v1</c> against
/// <c>1.0</c> is the canonical abstention: the numbers agree once the trailing zero is padded away,
/// which leaves only the change of notation to go on - and an author who changes notation is as
/// likely to have done it for the next release as for a rewrite of the same one. The same holds for
/// <c>1</c> against <c>1.0</c> and for <c>1.9</c> against <c>1.09</c>.
/// </item>
/// <item>
/// <b>A trailing segment the other string lacks counts only when it is non-zero.</b> <c>1.2.1</c>
/// follows <c>1.2</c> under any dotted scheme; <c>1.2.0</c> may well be <c>1.2</c> rewritten.
/// </item>
/// <item>
/// <b>Leading segments of wildly different magnitude are different schemes.</b> A date-like
/// <c>2024.03</c> next to a semantic <c>1.4</c> is not two thousand releases later, and nothing in
/// the strings says which came first. One digit of difference stays decidable, because that is
/// <c>9</c> against <c>10</c>.
/// </item>
/// <item>
/// <b>A pre-release is compared against its own release only.</b> <c>1.0-beta</c> precedes
/// <c>1.0</c>, and <c>rc1</c> precedes <c>rc2</c>, but two different labels are left undecided:
/// alphabetical order happens to get <c>beta</c> before <c>rc</c> and would just as confidently get
/// <c>rc</c> before <c>final</c>.
/// </item>
/// </list>
/// </remarks>
public sealed class DefaultModVersionComparer : IModVersionComparer
{
    /// <summary>
    /// Two digits of difference in the leading segment is the point where the pair stops looking
    /// like consecutive releases and starts looking like two numbering schemes.
    /// </summary>
    private const int MaxLeadingDigitDifference = 1;

    private static readonly char[] _suffixSeparators = [' ', '-', '_', '.'];


    public static DefaultModVersionComparer Instance { get; } = new();


    public ModVersionComparison Compare(ModVersionKey left, ModVersionKey right)
    {
        if (left == right)
        {
            return ModVersionComparison.Equal;
        }

        if (!ParsedVersion.TryParse(left.Value, out var parsedLeft) || !ParsedVersion.TryParse(right.Value, out var parsedRight))
        {
            return ModVersionComparison.Undecidable;
        }

        var leadingDigitDifference = Math.Abs(DigitCount(parsedLeft.Segments[0]) - DigitCount(parsedRight.Segments[0]));
        if (leadingDigitDifference > MaxLeadingDigitDifference)
        {
            return ModVersionComparison.Undecidable;
        }

        var bySegments = CompareSegments(parsedLeft.Segments, parsedRight.Segments);
        if (bySegments != ModVersionComparison.Equal)
        {
            return bySegments;
        }

        if (!parsedLeft.IsWrittenLike(parsedRight))
        {
            return ModVersionComparison.Undecidable;
        }

        return ComparePreRelease(parsedLeft, parsedRight);
    }


    private static ModVersionComparison CompareSegments(IReadOnlyList<long> left, IReadOnlyList<long> right)
    {
        var shared = Math.Min(left.Count, right.Count);

        for (var i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
            {
                return left[i] < right[i]
                    ? ModVersionComparison.Earlier
                    : ModVersionComparison.Later;
            }
        }

        for (var i = shared; i < left.Count; i++)
        {
            if (left[i] != 0)
            {
                return ModVersionComparison.Later;
            }
        }

        for (var i = shared; i < right.Count; i++)
        {
            if (right[i] != 0)
            {
                return ModVersionComparison.Earlier;
            }
        }

        return ModVersionComparison.Equal;
    }

    private static ModVersionComparison ComparePreRelease(ParsedVersion left, ParsedVersion right)
    {
        // A suffix nobody can read is not a reason to distrust the numbers - 1.0-rc.1 still precedes
        // 1.1 - but with the numbers equal it is the only thing left, and it says nothing.
        if (left.HasUnreadableSuffix || right.HasUnreadableSuffix)
        {
            return ModVersionComparison.Undecidable;
        }

        if (left.PreReleaseLabel is null && right.PreReleaseLabel is null)
        {
            return ModVersionComparison.Equal;
        }

        if (left.PreReleaseLabel is null)
        {
            return ModVersionComparison.Later;
        }

        if (right.PreReleaseLabel is null)
        {
            return ModVersionComparison.Earlier;
        }

        if (!string.Equals(left.PreReleaseLabel, right.PreReleaseLabel, StringComparison.OrdinalIgnoreCase))
        {
            return ModVersionComparison.Undecidable;
        }

        if (left.PreReleaseNumber is null || right.PreReleaseNumber is null)
        {
            return left.PreReleaseNumber == right.PreReleaseNumber
                ? ModVersionComparison.Equal
                : ModVersionComparison.Undecidable;
        }

        if (left.PreReleaseNumber == right.PreReleaseNumber)
        {
            return ModVersionComparison.Equal;
        }

        return left.PreReleaseNumber < right.PreReleaseNumber
            ? ModVersionComparison.Earlier
            : ModVersionComparison.Later;
    }

    private static int DigitCount(long value) =>
        value.ToString(CultureInfo.InvariantCulture).Length;


    /// <param name="RawSegments">
    /// The segments as written. Kept because the numbers alone cannot tell <c>1.9</c> from
    /// <c>1.09</c>, which is a difference in notation and therefore a reason to abstain.
    /// </param>
    private readonly record struct ParsedVersion(
        bool HasPrefix,
        IReadOnlyList<long> Segments,
        IReadOnlyList<string> RawSegments,
        string? PreReleaseLabel,
        long? PreReleaseNumber,
        bool HasUnreadableSuffix)
    {
        public bool IsWrittenLike(ParsedVersion other) =>
            HasPrefix == other.HasPrefix
            && RawSegments.Count == other.RawSegments.Count
            && RawSegments.SequenceEqual(other.RawSegments, StringComparer.Ordinal);

        public static bool TryParse(string value, out ParsedVersion parsed)
        {
            parsed = default;

            var text = value.AsSpan().Trim();
            if (text.IsEmpty)
            {
                return false;
            }

            var index = 0;
            var hasPrefix = false;

            if ((text[0] is 'v' or 'V') && text.Length > 1 && char.IsAsciiDigit(text[1]))
            {
                hasPrefix = true;
                index = 1;
            }

            var segments = new List<long>();
            var rawSegments = new List<string>();

            while (true)
            {
                var start = index;
                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    return false;
                }

                var raw = text[start..index];
                if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var segment))
                {
                    return false;
                }

                segments.Add(segment);
                rawSegments.Add(raw.ToString());

                // A dot not followed by a digit belongs to whatever suffix comes next, not to the
                // number.
                if (index + 1 < text.Length && text[index] == '.' && char.IsAsciiDigit(text[index + 1]))
                {
                    index++;
                    continue;
                }

                break;
            }

            ParseSuffix(text[index..], out var label, out var number, out var unreadable);

            parsed = new ParsedVersion(hasPrefix, segments, rawSegments, label, number, unreadable);
            return true;
        }

        private static void ParseSuffix(ReadOnlySpan<char> suffix, out string? label, out long? number, out bool unreadable)
        {
            label = null;
            number = null;
            unreadable = false;

            suffix = suffix.TrimStart(_suffixSeparators);
            if (suffix.IsEmpty)
            {
                return;
            }

            var letters = 0;
            while (letters < suffix.Length && char.IsAsciiLetter(suffix[letters]))
            {
                letters++;
            }

            var digits = letters;
            while (digits < suffix.Length && char.IsAsciiDigit(suffix[digits]))
            {
                digits++;
            }

            if (letters == 0 || digits != suffix.Length)
            {
                unreadable = true;
                return;
            }

            if (digits > letters)
            {
                if (!long.TryParse(suffix[letters..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedNumber))
                {
                    unreadable = true;
                    return;
                }

                number = parsedNumber;
            }

            label = suffix[..letters].ToString();
        }
    }
}
