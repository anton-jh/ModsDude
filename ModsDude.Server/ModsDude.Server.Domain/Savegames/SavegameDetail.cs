namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// One fact a client's game adapter chose to record about a version of a savegame - the map it was
/// played on, when, for how long - already worded for a person to read.
/// </summary>
/// <remarks>
/// <para>
/// <b>The server never parses one.</b> Same bargain as <see cref="Mods.ModAttribute"/> and as the
/// repo's adapter configuration: it is produced and consumed entirely by the client's adapter layer,
/// and that opacity is what lets a new game describe its saves however it likes without a server
/// deployment. Farming Simulator has a map and a difficulty; another game has a seed and a chapter;
/// a third has neither.
/// </para>
/// <para>
/// <b>Nothing may depend on one.</b> A fact the system needs in order to behave correctly is a real
/// property with a real column - <see cref="SavegameVersion.ContentHash"/> and
/// <see cref="SavegameVersion.ProfileRevision"/> are what that looks like. These exist to be
/// displayed, and a client that ignores them entirely is still correct.
/// </para>
/// <para>
/// <b>On the version, not the savegame.</b> A map, a playtime and a money balance describe the
/// bytes somebody checked in, not the savegame that has been carrying them for a year. Two versions
/// of one save legitimately disagree about every one of these.
/// </para>
/// </remarks>
public class SavegameDetail(
    string key,
    string label,
    string value,
    int position)
{
    /// <summary>
    /// Stable, machine-readable, and never rendered. It is what makes a fact findable later - if one
    /// of these turns out to be worth promoting to a real column, the recorded values can be
    /// migrated rather than parsed back out of a sentence.
    /// </summary>
    public string Key { get; init; } = key;

    /// <summary>What to print beside the value. Prose, and safe to reword.</summary>
    public string Label { get; init; } = label;

    /// <summary>Already formatted by the adapter that produced it.</summary>
    public string Value { get; init; } = value;

    /// <summary>
    /// Where this sits in the adapter's own ordering. Stored because "map, then when, then how long"
    /// is a judgment the adapter made and the reader benefits from, and a set has no order to
    /// recover it from.
    /// </summary>
    public int Position { get; init; } = position;
}
