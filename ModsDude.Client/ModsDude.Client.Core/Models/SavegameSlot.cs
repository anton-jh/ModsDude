namespace ModsDude.Client.Core.Models;

/// <summary>
/// One addressable place a savegame can live in one instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Slots are a list, never a count.</b> Farming Simulator has twenty numbered folders; another
/// game names its saves freely and has as many as somebody made. Both are this: an adapter-supplied
/// list of containers, some occupied. Nothing outside an adapter ever learns how many there are or
/// what they are called underneath.
/// </para>
/// <para>
/// <b>The slot is not the identity of a savegame.</b> The same shared save is slot 3 on one machine
/// and slot 7 on another, so what a slot holds is recorded locally against the server's savegame id
/// - see the checkout binding in <c>LocalState</c>. A slot id only ever addresses a place on this
/// machine.
/// </para>
/// </remarks>
/// <param name="DisplayName">
/// What the <em>game</em> calls the save that is in this slot, read out of the save itself. This is
/// what a picker shows, because the folder number is an implementation detail the player has never
/// thought in. Null where the slot is empty, or where the save could not be read - a slot whose
/// contents are unreadable is still a slot, and still occupied.
/// </param>
/// <param name="Details">
/// Whatever the adapter thinks is worth saying about the save in this slot, in the order it wants
/// them read - the map, when it was last played, how long for. Shown beside the name so somebody
/// can tell two farms apart, and <b>never depended on</b>: see <see cref="SavegameDetail"/>. Empty
/// for a slot that is free, or whose contents could not be read.
/// </param>
public record SavegameSlot(
    SavegameSlotId Id,
    string? DisplayName,
    bool IsOccupied,
    IReadOnlyList<SavegameDetail> Details);


/// <summary>
/// An adapter's address for one slot - a folder name for Farming Simulator, a save name elsewhere.
/// Opaque outside the adapter that minted it, and persisted as written.
/// </summary>
public readonly record struct SavegameSlotId(string Value)
{
    public override string ToString() => Value;
}
