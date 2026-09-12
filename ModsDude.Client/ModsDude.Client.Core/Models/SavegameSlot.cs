using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// One addressable place a savegame can live in one game.
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
/// can tell two savegames apart, and <b>never depended on</b>: see <see cref="SavegameDetail"/>. Empty
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


/// <summary>
/// One slot in one target - what addresses a place for a savegame once a game reaches more than one
/// folder.
/// </summary>
/// <remarks>
/// <para>
/// A compound value in the shape <see cref="GameAdapters.GameIdentity"/> and
/// <see cref="ActiveProfile"/> already have, rather than a prefixed string, which would invite
/// parsing at every site that handled one. <c>{target}:{slot}</c> exists only as the persisted
/// rendering, exactly as <c>_farming_simulator#fs25</c> is for an identity, and nothing but
/// <see cref="Parse"/> ever splits one.
/// </para>
/// <para>
/// <b>Uniqueness across a game is a construction rather than a contract.</b> An adapter mints slot
/// ids unique within its own target, which it cannot get wrong, and nothing has to be asked of it or
/// tested. Asking instead for ids unique across the game would put two targets' slots on one binding
/// the first time somebody numbered from one twice, and the one-binding-per-slot rule in
/// <c>SavegameBindingStore</c> would enforce that collision rather than catch it.
/// </para>
/// </remarks>
public readonly record struct SavegameSlotRef
{
    public SavegameSlotRef(TargetKey target, SavegameSlotId slot)
    {
        if (string.IsNullOrEmpty(slot.Value))
        {
            throw new ArgumentException("A slot reference cannot carry a blank slot id.", nameof(slot));
        }

        Target = target;
        Slot = slot;
    }


    public TargetKey Target { get; }
    public SavegameSlotId Slot { get; }


    public override string ToString() => $"{Target}{TargetKey.Separator}{Slot}";


    /// <summary>
    /// Reads back what <see cref="ToString"/> wrote. Splits at the first separator only: the target
    /// key cannot contain one, and a slot id is an adapter's own string that may.
    /// </summary>
    public static SavegameSlotRef Parse(string s)
    {
        var separator = s.IndexOf(TargetKey.Separator, StringComparison.Ordinal);

        if (separator < 0)
        {
            throw new FormatException($"Invalid SavegameSlotRef string '{s}'");
        }

        return new(
            new TargetKey(s[..separator]),
            new SavegameSlotId(s[(separator + TargetKey.Separator.Length)..]));
    }
}
