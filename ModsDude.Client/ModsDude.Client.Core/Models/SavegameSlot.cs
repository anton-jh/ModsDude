using ModsDude.Client.Core.GameAdapters;
using System.Text.Json;
using System.Text.Json.Serialization;

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
[JsonConverter(typeof(SavegameSlotRefJsonConverter))]
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
    /// Whether two references address the same place on this disk.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>==</c>, and the difference matters.</b> Both halves are folder names on Windows,
    /// where two spellings are one place - so this compares case-insensitively, exactly as the slot
    /// safety check and the binding store always did with bare slot ids. Treating two spellings as
    /// two slots would let a write land on top of a save a binding was protecting; treating them as
    /// one at worst refuses a write that was fine. Record equality stays exact, because a key is a
    /// key wherever one is used to look something up.
    /// </remarks>
    public bool Addresses(SavegameSlotRef other)
        => string.Equals(Target.Value, other.Target.Value, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Slot.Value, other.Slot.Value, StringComparison.OrdinalIgnoreCase);

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


/// <summary>
/// A slot reference as <c>{target}:{slot}</c>, because a savegame binding carries one and local
/// state is where it is written down.
/// </summary>
/// <remarks>
/// Needed rather than merely tidy, for the reason <see cref="TargetKeyJsonConverter"/> is: a record
/// struct with a validating constructor and get-only properties goes out as a nested object and
/// comes back <em>blank</em>, which would read as a hold on a slot nothing can name. The rendering
/// is the one this type already prints, so a person reading <c>state.json</c> sees the same string
/// the log does.
/// </remarks>
public sealed class SavegameSlotRefJsonConverter : JsonConverter<SavegameSlotRef>
{
    public override SavegameSlotRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return SavegameSlotRef.Parse(reader.GetString() ?? throw new JsonException("Expected a slot reference string."));
    }

    public override void Write(Utf8JsonWriter writer, SavegameSlotRef value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}


/// <summary>
/// One slot of one game: which of its targets holds it, what to call that target, and what the
/// adapter says is in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit everything above the adapter deals in.</b> A game's slots are one flat list across
/// every savegame folder it reaches, because a person choosing where a save goes is choosing a
/// place and not a folder - and a slot without its target key is not a place, it is a number that
/// three folders all have.
/// </para>
/// <para>
/// <see cref="TargetName"/> is null where the game reaches one savegame folder, which is nearly all
/// of them: a picker that groups a single group is a heading saying what the page already said.
/// </para>
/// </remarks>
public sealed record GameSavegameSlot(SavegameSlotRef Ref, string? TargetName, SavegameSlot Slot)
{
    public SavegameSlotId Id => Slot.Id;
    public bool IsOccupied => Slot.IsOccupied;
    public string? DisplayName => Slot.DisplayName;
    public IReadOnlyList<SavegameDetail> Details => Slot.Details;
}
