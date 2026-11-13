using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Globalization;

namespace ModsDude.Client.Core.Savegames;

/// <summary>A savegame detail worded for the person reading it.</summary>
public sealed record SavegameDetailLine(string Label, string Text);


/// <summary>
/// Words savegame details in the reader's culture and time zone. One place, so a slot on disk and a
/// snapshot on the server read the same way for every game.
/// </summary>
public static class SavegameDetailText
{
    public static IReadOnlyList<SavegameDetailLine> Lines(IEnumerable<SavegameDetail> details, CultureInfo culture, TimeZoneInfo zone)
        => [.. details.Select(x => new SavegameDetailLine(x.Label, Format(x.Kind, x.Value, culture, zone)))];

    public static IReadOnlyList<SavegameDetailLine> Lines(IEnumerable<SavegameDetailDto> details, CultureInfo culture, TimeZoneInfo zone)
        => [.. details.Select(x => new SavegameDetailLine(x.Label, Format(x.Kind, x.Value, culture, zone)))];

    /// <summary>
    /// The value in the reader's words. A value not in its kind's form - which the server refuses, so
    /// only an old or hand-made record has one - is shown as it is.
    /// </summary>
    public static string Format(SavegameDetailKind kind, string value, CultureInfo culture, TimeZoneInfo zone)
    {
        switch (kind)
        {
            case SavegameDetailKind.Date
                when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                return date.ToString("d", culture);

            case SavegameDetailKind.Instant
                when DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant):
                return TimeZoneInfo.ConvertTime(instant, zone).ToString("g", culture);

            case SavegameDetailKind.Duration
                when long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes):
                // Hours past the first, because a save's playtime is measured in tens of them.
                return minutes < 60
                    ? string.Format(culture, "{0:N0} min", minutes)
                    : string.Format(culture, "{0:N0} h", minutes / 60.0);

            default:
                return value;
        }
    }
}
