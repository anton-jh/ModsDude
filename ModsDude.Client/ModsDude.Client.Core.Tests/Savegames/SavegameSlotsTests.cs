using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Tests.GameProcesses;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>What the slot picker sees.</summary>
public class SavegameSlotsTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _slot2 = SavegameHarness.Slot2;
    private static readonly SavegameSlotRef _client = SavegameHarness.Client;


    [Fact]
    public async Task A_free_slot_and_an_unrecognised_one_are_told_apart()
    {
        using var harness = new SavegameHarness();

        Assert.Equal(SavegameSlotAvailability.Free, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));

        harness.WriteSlotFile(_slot1, "somebody's own savegame");

        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// A check-out followed by nothing leaves the slot holding exactly what was written, which has to
    /// read as clean - if it did not, every check-out would immediately report unpublished play and
    /// the notice would be worthless.
    /// </summary>
    [Fact]
    public async Task A_slot_just_checked_out_into_reads_as_held_and_clean()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task The_picker_pre_selects_the_remembered_slot_and_falls_back_to_the_first_free_one()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        // Nothing remembered yet: the first free slot.
        Assert.Equal(_slot1, await harness.Slots.SuggestSlotAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot2, CancellationToken.None);
        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        // The hint survives the check-in that destroyed the binding - that asymmetry is its whole job.
        Assert.Equal(_slot2, await harness.Slots.SuggestSlotAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));

        // And when the remembered slot is taken by something else, the first free one instead. The
        // hint is left exactly as it was; nothing here repairs it.
        harness.WriteSlotFile(_slot2, "somebody's own savegame");

        Assert.Equal(_slot1, await harness.Slots.SuggestSlotAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));
    }

    [Fact]
    public async Task No_free_slot_pre_selects_nothing()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "one savegame");
        harness.WriteSlotFile(_slot2, "another savegame");

        Assert.Null(await harness.Slots.SuggestSlotAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));
    }

    /// <summary>
    /// Every slot of every folder, in one list, each one addressed - and named by folder only because
    /// this game has two. A picker cannot show "savegame1" twice and expect anybody to choose.
    /// </summary>
    [Fact]
    public async Task Slots_are_one_list_across_every_folder_the_game_reaches()
    {
        using var harness = new SavegameHarness();

        harness.AddSecondTarget();

        var slots = await harness.Slots.GetSlotsAsync(harness.Game, CancellationToken.None);

        Assert.Equal(
            [_slot1, _slot2, _client, Keys.Slot("savegame2", "client")],
            slots.Select(x => x.Ref));

        Assert.All(slots, x => Assert.False(string.IsNullOrWhiteSpace(x.TargetName)));
    }

    /// <summary>A game with one folder never mentions it, which is nearly every game there is.</summary>
    [Fact]
    public async Task A_game_with_one_folder_does_not_name_it()
    {
        using var harness = new SavegameHarness();

        Assert.All(
            await harness.Slots.GetSlotsAsync(harness.Game, CancellationToken.None),
            x => Assert.Null(x.TargetName));
    }

    /// <summary>
    /// The same rule, asked about a hold rather than about a slot - which is what the savegame list
    /// needs now that a game's holds are lines on it rather than a page of their own.
    /// </summary>
    [Fact]
    public void Naming_the_folder_a_hold_is_in_follows_the_one_folder_rule()
    {
        using var harness = new SavegameHarness();

        // One folder, so there is nothing to tell apart and nothing worth saying.
        Assert.Null(harness.Slots.DescribeFolder(harness.Game, _slot1.Target));

        harness.AddSecondTarget();

        Assert.NotNull(harness.Slots.DescribeFolder(harness.Game, _slot1.Target));
        Assert.NotNull(harness.Slots.DescribeFolder(harness.Game, _client.Target));
    }

    /// <summary>
    /// <b>And a folder the settings no longer name is always named, by its key.</b> That is the
    /// opposite of the rule above and deliberately so: the hold is stuck until somebody puts that
    /// field back, and the key is the only handle they have on which field it was. No adapter offers
    /// the folder any more, so there is nothing else to call it.
    /// </summary>
    [Fact]
    public void A_folder_the_settings_no_longer_name_is_named_anyway()
    {
        using var harness = new SavegameHarness();

        harness.AddSecondTarget();
        harness.RemoveSecondTarget();

        Assert.Equal(_client.Target.Value, harness.Slots.DescribeFolder(harness.Game, _client.Target));

        // And the folder that is still there goes back to being unnamed, because the game reaches
        // one again.
        Assert.Null(harness.Slots.DescribeFolder(harness.Game, _slot1.Target));
    }
}
