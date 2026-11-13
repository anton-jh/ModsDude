using ModsDude.Server.Domain.Exceptions;
using System.Globalization;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// One fact a client's game adapter chose to record about a snapshot of a savegame - the map it was
/// played on, when, for how long.
/// </summary>
/// <remarks>
/// <para>
/// <b>Typed, so every reader sees it in their own words.</b> A date, an instant or a duration is
/// stored in one invariant form and formatted by whoever reads it, in their culture and time zone -
/// never in the uploader's. Text is for what has no such form, like a map's name.
/// </para>
/// <para>
/// <b>The server checks only the form.</b> What the facts are is the client's adapter's business,
/// which is what lets a new game describe its saves however it likes without a server deployment.
/// </para>
/// <para>
/// <b>Nothing may depend on one.</b> A fact the system needs in order to behave correctly is a real
/// property with a real column - <see cref="SavegameSnapshot.ContentHash"/> and
/// <see cref="SavegameSnapshot.ProfileRevision"/> are what that looks like. These exist to be
/// displayed, and a client that ignores them entirely is still correct.
/// </para>
/// <para>
/// <b>On the snapshot, not the savegame.</b> A map and a playtime describe the bytes somebody checked
/// in, not the savegame that has been carrying them for a year.
/// </para>
/// </remarks>
public class SavegameDetail
{
    public SavegameDetail(string key, string label, SavegameDetailKind kind, string value, int position)
    {
        if (SavegameDetailValues.IsValid(kind, value) is false)
        {
            throw new DomainValidationException($"'{value}' is not a valid {kind} value for savegame detail '{key}'.");
        }

        Key = key;
        Label = label;
        Kind = kind;
        Value = value;
        Position = position;
    }


    /// <summary>
    /// Stable, machine-readable, and never rendered. It is what makes a fact findable later - if one
    /// of these turns out to be worth promoting to a real column, the recorded values can be
    /// migrated rather than parsed back out of a sentence.
    /// </summary>
    public string Key { get; init; }

    /// <summary>What to print beside the value. Prose, and safe to reword.</summary>
    public string Label { get; init; }

    public SavegameDetailKind Kind { get; init; }

    /// <summary>In the invariant form <see cref="Kind"/> names - see <see cref="SavegameDetailValues"/>.</summary>
    public string Value { get; init; }

    /// <summary>
    /// Where this sits in the adapter's own ordering. Stored because "map, then when, then how long"
    /// is a judgment the adapter made and the reader benefits from, and a set has no order to
    /// recover it from.
    /// </summary>
    public int Position { get; init; }
}


/// <summary>What kind of value a savegame detail carries, which says how a reader formats it.</summary>
public enum SavegameDetailKind
{
    /// <summary>Shown as it is.</summary>
    Text,

    /// <summary>A calendar day with no time or zone, as <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>A moment, in UTC, as ISO 8601 round-trip (<c>2026-11-13T18:04:00.0000000Z</c>).</summary>
    Instant,

    /// <summary>A length of time, in whole minutes.</summary>
    Duration
}


/// <summary>The invariant form each <see cref="SavegameDetailKind"/> is stored in.</summary>
public static class SavegameDetailValues
{
    public static bool IsValid(SavegameDetailKind kind, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return kind switch
        {
            SavegameDetailKind.Text => true,
            SavegameDetailKind.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            SavegameDetailKind.Instant => DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                && instant.Offset == TimeSpan.Zero,
            SavegameDetailKind.Duration => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _),
            _ => false
        };
    }
}
