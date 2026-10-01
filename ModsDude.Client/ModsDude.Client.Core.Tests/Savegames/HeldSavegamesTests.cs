using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
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

/// <summary>Which revision the savegames a game holds require of its mod folder.</summary>
public class HeldSavegamesTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;


    /// <summary>
    /// A game following a friend onto a past savegame is held on that revision by its own intent, with
    /// no savegame of its own to say so - and the apply and the drift check have to read it all the same.
    /// </summary>
    [Fact]
    public void A_game_pinned_by_its_own_intent_requires_that_revision()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);

        harness.Game.PersistedModel.PinnedRevision = 3;

        Assert.Equal(3, harness.HeldSavegames.GetRequiredRevision(harness.Game.Identity, harness.ProfileId));
        Assert.Null(harness.HeldSavegames.GetRequiredRevision(harness.Game.Identity, Guid.NewGuid()));
    }

    /// <summary>
    /// A held past savegame outranks the game's own pin: the apply table refuses anything else while
    /// it is out, so it is the revision the folder actually has to be on.
    /// </summary>
    [Fact]
    public async Task A_held_past_savegame_outranks_the_games_own_pin()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();
        harness.Game.PersistedModel.PinnedRevision = 2;

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(4, harness.HeldSavegames.GetRequiredRevision(harness.Game.Identity, harness.ProfileId));
    }
}
