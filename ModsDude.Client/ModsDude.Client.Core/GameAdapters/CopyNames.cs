using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using System.Text.RegularExpressions;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// Recognises the names Windows gives a second file where the first is already taken: a browser's
/// <c>x (1)</c> and Explorer's <c>x - Copy</c>, alone or combined.
/// </summary>
/// <remarks>
/// For adapters whose mod id is the file name, such a name is a separate mod that was meant to be
/// another version of <c>x</c>.
/// </remarks>
internal static partial class CopyNames
{
    private const string _englishCopySuffix = " - Copy";

    private static IReadOnlyList<string>? _copySuffixes;


    /// <summary>The mod <paramref name="modId"/> looks like a copy of, or null where it looks like its own name.</summary>
    public static ModKey? OriginalOf(ModKey modId, ILogger log)
        => OriginalStem(modId.Value, CopySuffixes(log)) is string original ? ModKey.From(original) : null;

    /// <summary>
    /// <paramref name="stem"/> with every copy marker stripped from its end, or null where it has none
    /// or nothing would be left.
    /// </summary>
    /// <param name="copySuffixes">What Explorer appends to a copy, such as <c>" - Copy"</c>. Compared ignoring case.</param>
    public static string? OriginalStem(string stem, IReadOnlyList<string> copySuffixes)
    {
        var current = stem;

        while (true)
        {
            var stripped = StripCounter(current) ?? StripSuffix(current, copySuffixes);

            if (stripped is null)
            {
                break;
            }

            current = stripped;
        }

        current = current.TrimEnd();

        return current.Length > 0 && current.Length < stem.TrimEnd().Length ? current : null;
    }

    /// <summary>
    /// What Explorer appends to a copy, in English and in the language Windows runs in. English always,
    /// because a copy made on somebody else's machine travels with its name.
    /// </summary>
    private static IReadOnlyList<string> CopySuffixes(ILogger log)
        => LazyInitializer.EnsureInitialized(ref _copySuffixes, () => ReadCopySuffixes(log));


    private static IReadOnlyList<string> ReadCopySuffixes(ILogger log)
    {
        var template = ExplorerCopyName.ReadTemplate();
        var local = ExplorerCopyName.SuffixOf(template);

        if (local is null)
        {
            log.LogWarning("Windows' copy name template '{Template}' is not one this reads; only '{Suffix}' marks a copy.",
                template, _englishCopySuffix);

            return [_englishCopySuffix];
        }

        return string.Equals(local, _englishCopySuffix, StringComparison.OrdinalIgnoreCase)
            ? [_englishCopySuffix]
            : [_englishCopySuffix, local];
    }

    private static string? StripCounter(string value)
    {
        var match = Counter().Match(value);

        return match.Success ? value[..match.Index] : null;
    }

    private static string? StripSuffix(string value, IReadOnlyList<string> copySuffixes)
    {
        foreach (var suffix in copySuffixes)
        {
            if (suffix.Length > 0 && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return value[..^suffix.Length];
            }
        }

        return null;
    }

    [GeneratedRegex(@"\s*\(\d+\)\s*$")]
    private static partial Regex Counter();
}
