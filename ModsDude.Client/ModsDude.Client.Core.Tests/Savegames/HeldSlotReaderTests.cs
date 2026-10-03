using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>Hashing the slots this machine holds savegames in, for drift and play attribution.</summary>
public class HeldSlotReaderTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _client = SavegameHarness.Client;


    [Fact]
    public async Task A_held_slot_reads_as_its_current_hash_and_name()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        harness.Adapter.DisplayNames[_slot1.Slot.Value] = "My farm";

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);
        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var reading = Assert.Single(await harness.Reader.ReadAsync(harness.Game.Identity, CancellationToken.None));

        Assert.Equal(harness.Server.SavegameId, reading.Binding.SavegameId);
        Assert.Equal(await harness.HashSlotAsync(_slot1), reading.CurrentHash);
        Assert.Equal("My farm", reading.SlotDisplayName);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_read_reads_as_no_hash_rather_than_failing()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.Adapter.ThrowOnGetSlots = true;

        var reading = Assert.Single(await harness.Reader.ReadAsync(harness.Game.Identity, CancellationToken.None));

        Assert.Null(reading.CurrentHash);
        Assert.Null(reading.SlotDisplayName);
    }

    [Fact]
    public async Task A_hold_in_a_folder_the_settings_no_longer_name_is_left_out()
    {
        using var harness = new SavegameHarness();
        harness.AddSecondTarget();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _client, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.RemoveSecondTarget();

        Assert.Empty(await harness.Reader.ReadAsync(harness.Game.Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_holds_asked_about_are_read()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        Assert.Empty(await harness.Reader.ReadAsync(harness.Game.Identity, [], CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_read_stops_rather_than_reporting_nothing()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Reader.ReadAsync(harness.Game.Identity, new CancellationToken(canceled: true)));
    }
}
