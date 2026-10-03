using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Tests.GameProcesses;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>Publishing a slot as a new savegame.</summary>
public class SavegamePublisherTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _slot2 = SavegameHarness.Slot2;


    /// <summary>
    /// A newly published savegame is held on latest, so it follows the profile from then on rather
    /// than staying on the revision it was published at.
    /// </summary>
    [Fact]
    public async Task Publishing_leaves_the_mod_folder_pinned_to_nothing()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        var (savegame, _) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: true, CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.Publishes).ProfileRevision);
        Assert.Null(harness.Binding(savegame.Id).TargetRevision);
    }

    /// <summary>
    /// The same limit reached from the other side rather than a rule of its own: a publish opens a
    /// claim in the same transaction as the savegame, so publishing to a profile would leave this
    /// game holding two savegames that both want its mod folder.
    /// </summary>
    [Fact]
    public async Task Publishing_to_a_profile_while_a_savegame_is_held_is_refused()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot2, "a brand new savegame");

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Publisher.PublishAsync(
                harness.Game, harness.Server.RepoId, _slot2, "Season 5", null, harness.Target(), keepPlaying: true, CancellationToken.None));

        Assert.Contains("already holding a savegame", exception.UserMessage);

        // Refused before the bytes were packed, so nothing was uploaded and no orphan blob was left
        // for the reclamation sweep.
        Assert.Empty(harness.Server.Publishes);
        Assert.Equal(0, harness.Uploader.Uploads);
    }

    [Fact]
    public async Task Publishing_uploads_the_slot_mints_the_savegame_and_leaves_this_machine_holding_it()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        var (savegame, _) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", "the beginning", harness.Target(), keepPlaying: true, CancellationToken.None);

        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.Equal("Season 5", savegame.Name);

        var request = Assert.Single(harness.Server.Publishes);

        // The id is minted client-side, because the blob is addressed by it and has to be uploadable
        // before the savegame exists.
        Assert.NotEqual(Guid.Empty, request.SavegameId);
        Assert.Equal(savegame.Id, request.SavegameId);

        // The pair the dialog settled, sent as one: the profile chosen there, and the revision it
        // declared - which for a folder already on that profile is the revision the folder is on.
        Assert.Equal(harness.ProfileId, request.ProfileId);
        Assert.Equal(harness.AppliedRevision, request.ProfileRevision);

        // Publishing leaves you holding it - the server opens a claim, and this is its local half.
        var binding = harness.Bindings.GetBinding(harness.Game.Identity, savegame.Id);

        Assert.NotNull(binding);
        Assert.Equal(_slot1, binding.Value.Slot);
        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// The other ending, which is what publishing to a mod list this game is not on always takes: the
    /// snapshot is minted, the claim goes straight back, and the local copy goes to the Recycle Bin.
    /// Without it, one publish leaves a savegame following one profile checked out into a folder on
    /// another - drift no apply can clear, because the apply table refuses every profile the folder
    /// could move to.
    /// </summary>
    [Fact]
    public async Task Publishing_without_keeping_it_hands_the_savegame_straight_back()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        var (savegame, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        // The savegame is real and the snapshot was minted: this is a publish, not a cancelled one.
        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.Single(harness.Server.Publishes);

        // And nothing on this machine claims it any more.
        Assert.Equal(1, harness.Server.CheckoutsDiscarded);
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, savegame.Id));
        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
        Assert.Equal(SavegameLocalCopy.Recycled, localCopy);

        // Which is what leaves the mod folder free for the next savegame, rather than spoken for by
        // one that is no longer here.
        Assert.True(harness.HeldSavegames.DecideApply(harness.Game.Identity, Guid.NewGuid(), null).IsAllowed);
    }

    /// <summary>
    /// The shell refuses by returning rather than throwing, so this used to pass in silence while the
    /// caller told the user their copy was in the Recycle Bin. The hand-back itself still stands - the
    /// bytes are on the server and the claim is released - but the answer says the folder stayed.
    /// </summary>
    [Fact]
    public async Task Publishing_without_keeping_it_says_so_when_the_recycle_bin_refuses_the_local_copy()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");
        harness.RecycleBin.Refuses = true;

        var (savegame, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        Assert.Equal(SavegameLocalCopy.LeftBehind, localCopy);
        Assert.Equal(4, harness.RecycleBin.Attempts);

        Assert.Equal(1, harness.Server.CheckoutsDiscarded);
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, savegame.Id));

        // Left exactly as it was, and read as a save nothing tracks - which needs a confirmation to
        // displace, the safe way round.
        Assert.Equal("a brand new savegame", harness.ReadSlotFile(_slot1));
        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// A slot that was renamed and packed a moment ago is what a scanner or the indexer wakes up to
    /// read, and the move fails while they hold it. They let go; a refusal the first time is not the
    /// answer.
    /// </summary>
    [Fact]
    public async Task Publishing_without_keeping_it_asks_the_recycle_bin_again_when_it_refuses_at_first()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");
        harness.RecycleBin.RefusesFirst = 2;

        var (_, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        Assert.Equal(SavegameLocalCopy.Recycled, localCopy);
        Assert.Equal(3, harness.RecycleBin.Attempts);
        Assert.False(Directory.Exists(harness.SlotPath(_slot1)));
    }

    /// <summary>
    /// A first snapshot's revision is declared rather than observed, so a folder that has never been
    /// synced is not an obstacle: nothing knows which mods were in it while that savegame was played
    /// either way, and requiring a sync first would observe the folder at the moment of publishing -
    /// which is a different fact, not a better one.
    /// </summary>
    [Fact]
    public async Task Publishing_from_an_game_that_has_never_been_synced_declares_the_profile_head()
    {
        using var harness = new SavegameHarness(writeManifest: false);

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(headRevision: 7), keepPlaying: true, CancellationToken.None);

        var request = Assert.Single(harness.Server.Publishes);

        Assert.Equal(harness.ProfileId, request.ProfileId);
        Assert.Equal(7, request.ProfileRevision);
    }

    /// <summary>
    /// The other answer the picker offers, and the first thing on this client that publishes a savegame
    /// following no mod list at all. It records no revision, claims no mod folder, and is therefore
    /// not subject to the limit that refuses a second savegame.
    /// </summary>
    [Fact]
    public async Task Publishing_without_a_profile_records_neither_half_of_the_pair()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        // Already holding one that claims the mod folder, which a publish *to a profile* is refused
        // for. This one claims nothing.
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot2, "an unmanaged savegame");

        var (savegame, _) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot2, "Scratch", null, target: null, keepPlaying: true, CancellationToken.None);

        var request = Assert.Single(harness.Server.Publishes);

        Assert.Null(request.ProfileId);
        Assert.Null(request.ProfileRevision);

        var binding = harness.Binding(savegame.Id);

        Assert.Null(binding.ProfileId);
        Assert.Null(binding.ProfileRevision);
        Assert.Null(binding.TargetRevision);
    }

    [Fact]
    public async Task Publishing_writes_the_given_name_into_the_slot_before_packing()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: true, CancellationToken.None);

        Assert.Equal("Season 5", Assert.Single(harness.Adapter.Renames).Name);
    }

    [Fact]
    public async Task Publishing_is_refused_while_the_game_is_running()
    {
        using var harness = new SavegameHarness();
        harness.WriteSlotFile(_slot1, "a brand new savegame");
        harness.Guard.Running = true;

        await Assert.ThrowsAsync<GameRunningException>(() => harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: true, CancellationToken.None));

        Assert.Empty(harness.Server.Publishes);
        Assert.Empty(harness.Adapter.Renames);
    }
}
