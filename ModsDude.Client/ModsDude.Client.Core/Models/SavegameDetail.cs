using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// One fact an adapter chose to say about a savegame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Free-form, because the games are.</b> Farming Simulator has a map, a difficulty and a
/// playtime; another game has a seed, a chapter and a death count; a third has none of it. The adapter
/// decides what is worth saying and what to call it, and everything above simply renders the list.
/// </para>
/// <para>
/// <b>Typed, never pre-formatted.</b> A date, a moment or a length of time is carried in one
/// invariant form and worded by whoever reads it - see <see cref="Savegames.SavegameDetailText"/> - so
/// a snapshot checked in on a Swedish machine at 23:00 reads in the reader's own culture and time zone.
/// </para>
/// <para>
/// <b><see cref="Id"/> is not shown, and that is the point.</b> A label is prose and can be reworded;
/// the id is the stable name for "this is the map".
/// </para>
/// <para>
/// <b>Nothing may depend on one.</b> These are opaque, optional, and read only to be displayed.
/// </para>
/// </remarks>
/// <param name="Id">
/// Stable, machine-readable, lowercase, and never rendered. Adapter-scoped: two games are free to
/// use the same id for the same idea, and equally free not to.
/// </param>
/// <param name="Label">What to print beside the value. Prose, and safe to change.</param>
/// <param name="Value">
/// In the invariant form <paramref name="Kind"/> names: <c>yyyy-MM-dd</c> for a date, ISO 8601 round-trip
/// in UTC for an instant, whole minutes for a duration, and anything for text.
/// </param>
public record SavegameDetail(string Id, string Label, SavegameDetailKind Kind, string Value)
{
    /// <summary>
    /// Ids the reference adapter uses, kept together so that a second adapter describing the same
    /// idea can choose to match rather than inventing a synonym. Nothing enforces or requires them.
    /// </summary>
    public static class Ids
    {
        public const string Map = "map";
        public const string LastPlayed = "last-played";
        public const string Started = "started";
        public const string Playtime = "playtime";
        public const string Difficulty = "difficulty";
        public const string Multiplayer = "multiplayer";
    }
}
