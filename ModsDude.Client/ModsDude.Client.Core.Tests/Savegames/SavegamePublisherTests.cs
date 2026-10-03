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
    /// Publishing always works. Where the mod folder is already spoken for, the new savegame is simply
    /// not kept: no claim, no binding, and the slot goes to the Recycle Bin.
    /// </summary>
    [Fact]
    public async Task Publishing_without_keeping_it_while_another_savegame_is_held_leaves_that_one_alone()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        var heldId = harness.Server.SavegameId;

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);
        var held = harness.Binding(heldId);

        harness.WriteSlotFile(_slot2, "a brand new savegame");

        var (savegame, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot2, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        Assert.False(Assert.Single(harness.Server.Publishes).KeepPlaying);
        Assert.Equal(SavegameLocalCopy.Recycled, localCopy);
        Assert.Equal(harness.SlotPath(_slot2), Assert.Single(harness.RecycleBin.Recycled));

        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, savegame.Id));
        Assert.Equal(held, harness.Binding(heldId));
        Assert.Equal(0, harness.Server.CheckoutsDiscarded);
    }

    /// <summary>
    /// A publish opens its claim in the same transaction as the savegame, so keeping this one would
    /// leave the game holding two savegames that both want its mod folder.
    /// </summary>
    [Fact]
    public async Task Keeping_a_published_savegame_while_another_is_held_is_refused_before_anything_is_uploaded()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot2, "a brand new savegame");

        await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Publisher.PublishAsync(
                harness.Game, harness.Server.RepoId, _slot2, "Season 5", null, harness.Target(), keepPlaying: true, CancellationToken.None));

        Assert.Empty(harness.Server.Publishes);
        Assert.Equal(0, harness.Uploader.Uploads);
        Assert.Empty(harness.Adapter.Renames);
    }

    /// <summary>
    /// The save would follow one mod list while sitting in a folder on another, which is the state
    /// that damages saves.
    /// </summary>
    [Fact]
    public async Task Keeping_a_published_savegame_on_a_profile_the_game_is_not_on_is_refused()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Publisher.PublishAsync(
                harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, new SavegamePublishTarget(Guid.NewGuid(), 1),
                keepPlaying: true, CancellationToken.None));

        Assert.Empty(harness.Server.Publishes);
        Assert.Equal(0, harness.Uploader.Uploads);
    }

    /// <summary>
    /// The answer was lost after the server created the savegame. Publishing the same slot under the
    /// same name again is the same publish, and is answered as one rather than refused as a name taken.
    /// </summary>
    [Fact]
    public async Task Publishing_again_after_a_lost_answer_repeats_the_same_publish()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");
        harness.Server.LoseNextPublishAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None));

        // Nothing was recycled while the outcome was unknown.
        Assert.Equal("a brand new savegame", harness.ReadSlotFile(_slot1));

        var (savegame, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        Assert.Equal(2, harness.Server.Publishes.Count);
        Assert.Equal(harness.Server.Publishes[0].RequestId, harness.Server.Publishes[1].RequestId);
        Assert.Equal(harness.Server.Publishes[0].SavegameId, harness.Server.Publishes[1].SavegameId);
        Assert.Equal(harness.Server.Publishes[0].SavegameId, savegame.Id);
        Assert.Equal(SavegameLocalCopy.Recycled, localCopy);

        // Answered, so there is nothing left to repeat.
        Assert.Empty(harness.State.Find(harness.Game.Identity)!.SavegamePendingPublishes);
    }

    [Fact]
    public async Task Publishing_different_bytes_after_a_lost_answer_is_a_new_publish()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");
        harness.Server.LoseNextPublishAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None));

        harness.WriteSlotFile(_slot1, "played some more");

        await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 6", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        Assert.Equal(2, harness.Server.Publishes.Count);
        Assert.NotEqual(harness.Server.Publishes[0].RequestId, harness.Server.Publishes[1].RequestId);
        Assert.NotEqual(harness.Server.Publishes[0].SavegameId, harness.Server.Publishes[1].SavegameId);
    }

    /// <summary>
    /// The repeat asked not to keep the save, but the publish it repeats did keep it: the server holds
    /// a claim, so this machine has to hold the binding that goes with it.
    /// </summary>
    [Fact]
    public async Task A_repeated_publish_follows_the_server_answer_rather_than_its_own_request()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");
        harness.Server.LoseNextPublishAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: true, CancellationToken.None));

        var (savegame, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        Assert.Equal(SavegameLocalCopy.Kept, localCopy);
        Assert.Equal(_slot1, harness.Binding(savegame.Id).Slot);
        Assert.Empty(harness.RecycleBin.Recycled);
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
        Assert.NotEqual(Guid.Empty, request.RequestId);
        Assert.NotEqual(request.SavegameId, request.RequestId);
        Assert.True(request.KeepPlaying);

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
    /// The other ending: the snapshot is minted, no claim is ever opened, and the local copy goes to the
    /// Recycle Bin.
    /// </summary>
    [Fact]
    public async Task Publishing_without_keeping_it_opens_no_claim_and_recycles_the_slot()
    {
        using var harness = new SavegameHarness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        var (savegame, localCopy) = await harness.Publisher.PublishAsync(
            harness.Game, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), keepPlaying: false, CancellationToken.None);

        // The savegame is real and the snapshot was minted: this is a publish, not a cancelled one.
        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.False(Assert.Single(harness.Server.Publishes).KeepPlaying);

        // And nothing claims it, here or on the server.
        Assert.Null(harness.Server.Claim);
        Assert.Equal(0, harness.Server.CheckoutsDiscarded);
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, savegame.Id));
        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
        Assert.Equal(SavegameLocalCopy.Recycled, localCopy);

        // Which is what leaves the mod folder free for the next savegame, rather than spoken for by
        // one that is no longer here.
        Assert.True(harness.HeldSavegames.DecideApply(harness.Game.Identity, Guid.NewGuid(), null).IsAllowed);
    }

    /// <summary>
    /// The shell refuses by returning rather than throwing. The publish itself still stands - the
    /// bytes are on the server and nothing claims them - but the answer says the folder stayed.
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

        Assert.Null(harness.Server.Claim);
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

        // Already holding one that claims the mod folder, which keeping a publish *to a profile* is
        // refused for. This one claims nothing.
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
