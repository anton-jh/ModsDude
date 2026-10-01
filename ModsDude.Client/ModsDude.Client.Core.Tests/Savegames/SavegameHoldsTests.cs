using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameProcesses;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>Making a savegame current again, and holds in a folder the settings no longer name.</summary>
public class SavegameHoldsTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _client = SavegameHarness.Client;


    /// <summary>
    /// A past savegame made current again follows its profile from here, so the pin that held this
    /// game's mod folder at revision 4 has to go with it.
    /// </summary>
    /// <remarks>
    /// The one case where a hold <em>does</em> move under its holder, and it is not a contradiction:
    /// "decided once" is about somebody else's publish, which nobody states to whoever is playing.
    /// This is the same swap stated to the person performing it. Left behind, the number would hold
    /// the folder at revision 4 forever and refuse every apply that tried to move it forward.
    /// </remarks>
    [Fact]
    public async Task Making_a_past_savegame_current_lets_go_of_the_revision_it_pinned()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).TargetRevision);

        await harness.Holds.MakeCurrentAsync([harness.Game], harness.Server.Savegame, CancellationToken.None);

        Assert.Equal(1, harness.Server.MadeCurrent);
        Assert.Null(harness.Binding(harness.Server.SavegameId).TargetRevision);

        // And with the pin gone, the apply table stops refusing head - which is the whole point of
        // clearing it.
        Assert.True(harness.HeldSavegames.DecideApply(harness.Game.Identity, harness.ProfileId, 1004).IsAllowed);
    }

    /// <summary>
    /// Nothing here is holding it, which is the ordinary case: the swap is about a savegame, and the
    /// pin is a fact about a mod folder that may be on somebody else's machine entirely.
    /// </summary>
    [Fact]
    public async Task Making_a_savegame_current_touches_no_game_that_is_not_holding_it()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();

        await harness.Holds.MakeCurrentAsync([harness.Game], harness.Server.Savegame, CancellationToken.None);

        Assert.Equal(1, harness.Server.MadeCurrent);
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId));
    }

    /// <summary>
    /// <b>The orphan that is not droppable.</b> A settings field somebody emptied - or an adapter
    /// author renaming a key, which is the same event from here - takes away the folder a checked-out
    /// save is sitting in. The hold is kept, because it is a savegame on this disk and a claim
    /// somebody else is waiting on; it is reported as unreachable, because there is no folder to pack
    /// and pretending otherwise would offer a check-in that cannot work.
    /// </summary>
    [Fact]
    public async Task A_hold_in_a_folder_the_settings_no_longer_name_survives_and_is_reported()
    {
        using var harness = new SavegameHarness();

        harness.AddSecondTarget();

        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _client, CancellationToken.None);

        harness.RemoveSecondTarget();

        // Still held, and still named by the binding.
        Assert.Equal(_client, harness.Binding(harness.Server.SavegameId).Slot);

        var unreachable = Assert.Single(harness.Holds.GetUnreachableHolds(harness.Game));

        Assert.Equal(harness.Server.SavegameId, unreachable.SavegameId);

        // And it is not a slot any more: nothing lists it, and nothing that touches the bytes will
        // pretend it can find them.
        var slots = await harness.Slots.GetSlotsAsync(harness.Game, CancellationToken.None);

        Assert.DoesNotContain(slots, x => x.Ref.Target == _client.Target);

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(() => harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None));

        Assert.Contains("no longer has the folder", exception.UserMessage);
    }

    /// <summary>
    /// And it reports no drift while it is unreachable, rather than guessing. There is no folder to
    /// hash, so "this has been played and never checked in" is not something anything here knows -
    /// and a warning that fires on a folder nobody can look at is one people learn to click past.
    /// </summary>
    [Fact]
    public async Task A_hold_in_a_folder_the_settings_no_longer_name_reports_no_drift()
    {
        using var harness = new SavegameHarness();

        harness.AddSecondTarget();

        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _client, CancellationToken.None);

        // Played, which in a folder that was still configured would be reported at once.
        harness.WriteSlotFile(_client, "a savegame, played once");

        harness.RemoveSecondTarget();

        Assert.Empty(await harness.DriftCheck.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));
    }

    /// <summary>
    /// Filling the field back in is all it takes. Nothing was dropped, so nothing has to be recovered
    /// - which is the whole argument for keeping a binding a settings edit orphaned.
    /// </summary>
    [Fact]
    public async Task Putting_the_folder_back_makes_the_hold_addressable_again()
    {
        using var harness = new SavegameHarness();

        harness.AddSecondTarget();

        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _client, CancellationToken.None);

        harness.RemoveSecondTarget();
        harness.AddSecondTarget();

        Assert.Empty(harness.Holds.GetUnreachableHolds(harness.Game));

        await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Single(harness.Server.CheckIns);
    }
}
