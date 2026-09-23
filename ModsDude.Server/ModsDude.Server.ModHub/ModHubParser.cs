using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.ModHub;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModsDude.Server.ModHub;

/// <summary>
/// Reads the two kinds of ModHub page the crawler needs. Pure, so it can be held against saved pages.
/// </summary>
/// <remarks>
/// <b>A page it does not recognise is an exception, never an empty answer.</b> The day ModHub is
/// redesigned, a parser that returned nothing would have the crawler conclude the listing had ended and
/// every mod been removed.
/// </remarks>
public static partial class ModHubParser
{
    private static readonly HtmlParser _parser = new();


    /// <summary>
    /// The ids of a "latest" listing page in order, from the grid only - the featured mods at the top
    /// of the first page are not part of the order. Empty past the last page.
    /// </summary>
    public static IReadOnlyList<int> ParseLatestPage(string html)
    {
        using var document = _parser.ParseDocument(html);

        // On every listing page, past-the-end included, so it tells an empty page from a strange one.
        if (document.QuerySelector(".mod-search-box") is null)
        {
            throw new ModHubUnreadableException("A listing page has no search box; it does not look like a ModHub listing.");
        }

        var ids = new List<int>();

        foreach (var item in document.QuerySelectorAll("div.mod-item"))
        {
            var href = item.QuerySelector("a[href*='mod_id=']")?.GetAttribute("href")
                ?? throw new ModHubUnreadableException("A listed mod has no link to its page.");

            ids.Add(ParseModId(href));
        }

        return ids;
    }

    /// <summary>What a mod page says, or null where it is ModHub's "mod not found" page.</summary>
    public static ModHubModDetails? ParseModPage(string html)
    {
        using var document = _parser.ParseDocument(html);

        var title = Text(document.QuerySelector("h2.title-label"));
        var info = document.QuerySelector(".table-game-info");

        if (info is null)
        {
            // ModHub answers a missing mod with 200 and an error heading, not a 404.
            return title?.StartsWith("Error", StringComparison.OrdinalIgnoreCase) is true
                ? null
                : throw new ModHubUnreadableException("A mod page has neither the mod's details nor ModHub's error heading.");
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in info.QuerySelectorAll(".table-row"))
        {
            var cells = row.QuerySelectorAll(".table-cell");

            if (cells.Length >= 2 && Text(cells[0]) is { Length: > 0 } label)
            {
                fields.TryAdd(label, Text(cells[1]) ?? "");
            }
        }

        var fileName = Required(fields, "Filename");
        var version = Required(fields, "Version");

        return new ModHubModDetails(
            FileName: fileName,
            Title: string.IsNullOrEmpty(title) ? fileName : title,
            Author: Optional(fields, "Author"),
            Version: version,
            Released: ParseDate(Optional(fields, "Released")),
            Size: Optional(fields, "Size"),
            DownloadUrl: document.QuerySelectorAll("a[href*='/modHub/storage/']")
                .Select(x => x.GetAttribute("href"))
                .FirstOrDefault(x => x?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) is true));
    }


    private static int ParseModId(string href)
    {
        var match = ModIdPattern().Match(href);

        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new ModHubUnreadableException($"A mod link '{href}' carries no mod id.");
    }

    private static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static string Required(Dictionary<string, string> fields, string label)
    {
        return Optional(fields, label)
            ?? throw new ModHubUnreadableException($"A mod page has no '{label}'.");
    }

    private static string? Optional(Dictionary<string, string> fields, string label)
    {
        return fields.TryGetValue(label, out var value) && value.Length > 0 ? value : null;
    }

    private static string? Text(IElement? element)
    {
        return element?.TextContent.Trim();
    }


    [GeneratedRegex(@"[?&]mod_id=(\d+)")]
    private static partial Regex ModIdPattern();
}
