namespace ModsDude.Client.Core.Models;

/// <summary>
/// One fact an adapter chose to say about a savegame, already worded for a person to read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Free-form, because the games are.</b> Farming Simulator has a map, a difficulty and a money
/// balance; another game has a seed, a chapter and a death count; a third has none of it. Modelling
/// any of that as columns would mean either a schema with a hole in it for every game nobody wrote
/// yet, or an adapter contract that grows a field per game. The adapter decides what is worth
/// saying and what to call it, and everything above simply renders the list.
/// </para>
/// <para>
/// <b><see cref="Id"/> is not shown, and that is the point.</b> A label is prose - it can be
/// reworded, translated, or shortened because a column got narrow - and prose is not something to
/// key on. The id is the stable name for "this is the map", so that if a fact turns out to be worth
/// promoting to a real property later, the values already recorded can be found and migrated
/// rather than parsed back out of a sentence.
/// </para>
/// <para>
/// <b>Nothing may depend on one.</b> Same rule the mod catalog's attributes follow: these are
/// opaque, optional, written by whichever adapter produced them, and read only to be displayed. A
/// fact the system needs in order to behave correctly is a real property with a real column -
/// <c>ContentHash</c> and <c>ProfileRevision</c> are what that looks like.
/// </para>
/// </remarks>
/// <param name="Id">
/// Stable, machine-readable, lowercase, and never rendered. Adapter-scoped: two games are free to
/// use the same id for the same idea, and equally free not to.
/// </param>
/// <param name="Label">What to print beside the value. Prose, and safe to change.</param>
/// <param name="Value">Already formatted. The adapter knows what a playtime or a money balance means.</param>
public record SavegameDetail(string Id, string Label, string Value)
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
        public const string Money = "money";
        public const string Difficulty = "difficulty";
        public const string Multiplayer = "multiplayer";
    }
}
