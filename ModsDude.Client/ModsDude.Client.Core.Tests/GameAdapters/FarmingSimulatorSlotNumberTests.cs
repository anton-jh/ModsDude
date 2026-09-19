using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// Farming Simulator's slots are folders called <c>savegame1</c> to <c>savegame20</c>, and its own
/// menu calls them slots 1 to 20 - so the number is what a player says, and what every surface that
/// names a slot has to be able to say.
/// </summary>
public class FarmingSimulatorSlotNumberTests : IDisposable
{
    private readonly TempDirectory _gameData = new("fs-slots");


    [Theory]
    [InlineData("savegame1", 1)]
    [InlineData("savegame7", 7)]
    [InlineData("savegame20", 20)]
    [InlineData("SAVEGAME7", 7)]
    public void A_savegame_folder_is_the_slot_it_is_numbered(string id, int number)
    {
        Assert.Equal(number, Adapter().GetSlotNumber(new SavegameSlotId(id)));
    }

    /// <summary>
    /// A number an adapter cannot vouch for is left off rather than invented: "slot 0" or "slot 12x"
    /// on a row would be a claim about the game that nothing in it makes.
    /// </summary>
    [Theory]
    [InlineData("savegame0")]
    [InlineData("savegame")]
    [InlineData("savegame1x")]
    [InlineData("savegame-3")]
    [InlineData("backup")]
    public void Anything_else_has_no_number(string id)
    {
        Assert.Null(Adapter().GetSlotNumber(new SavegameSlotId(id)));
    }

    [Fact]
    public async Task Every_slot_the_game_offers_carries_its_number()
    {
        var adapter = Adapter();
        var target = adapter.SavegameTargets.Single();

        var slots = await adapter.GetSlots(target, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 20), slots.Select(x => x.Number!.Value));
        Assert.All(slots, slot => Assert.Equal(adapter.GetSlotNumber(slot.Id), slot.Number));
    }

    /// <summary>
    /// The number belongs to the place and not to what is in it: an empty slot is slot 4 too, and it is
    /// the one somebody is choosing between when they pick where a save goes.
    /// </summary>
    [Fact]
    public async Task An_empty_slot_has_a_number_as_well()
    {
        var adapter = Adapter();
        var slots = await adapter.GetSlots(adapter.SavegameTargets.Single(), CancellationToken.None);

        var empty = slots.First(x => x.Id.Value == "savegame4");

        Assert.False(empty.IsOccupied);
        Assert.Equal(4, empty.Number);
    }


    private FarmingSimulatorLocalSavegameAdapter Adapter()
        => new(new FarmingSimulatorLocalSettings { GameDataFolder = _gameData.Path });

    public void Dispose() => _gameData.Dispose();
}
