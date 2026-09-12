using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Models;

/// <summary>
/// A slot address is a pair, and <c>{target}:{slot}</c> is only how it is written down.
/// </summary>
public class SavegameSlotRefTests
{
    [Fact]
    public void A_reference_round_trips_through_its_persisted_rendering()
    {
        var slot = new SavegameSlotRef(new TargetKey("mods"), new SavegameSlotId("savegame3"));

        Assert.Equal("mods:savegame3", slot.ToString());
        Assert.Equal(slot, SavegameSlotRef.Parse(slot.ToString()));
    }

    /// <summary>
    /// A slot id is an adapter's own opaque string and may well carry a colon - a save named after a
    /// time of day. The target key may not, which is what makes the first separator the right one.
    /// </summary>
    [Fact]
    public void A_slot_id_carrying_the_separator_survives()
    {
        var slot = new SavegameSlotRef(new TargetKey("client"), new SavegameSlotId("18:00 harvest"));

        Assert.Equal(slot, SavegameSlotRef.Parse(slot.ToString()));
        Assert.Equal("18:00 harvest", SavegameSlotRef.Parse(slot.ToString()).Slot.Value);
    }

    /// <summary>
    /// The property the whole compound shape buys: two targets numbering their slots from one are two
    /// different savegames, rather than one binding enforcing a collision.
    /// </summary>
    [Fact]
    public void The_same_slot_id_in_two_targets_is_two_references()
    {
        Assert.NotEqual(
            new SavegameSlotRef(new TargetKey("server"), new SavegameSlotId("savegame1")),
            new SavegameSlotRef(new TargetKey("client"), new SavegameSlotId("savegame1")));
    }

    [Fact]
    public void A_rendering_with_no_target_is_refused()
    {
        Assert.Throws<FormatException>(() => SavegameSlotRef.Parse("savegame3"));
    }

    [Fact]
    public void A_reference_to_no_slot_at_all_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new SavegameSlotRef(new TargetKey("mods"), new SavegameSlotId("")));
        Assert.Throws<ArgumentException>(() => SavegameSlotRef.Parse("mods:"));
    }
}
