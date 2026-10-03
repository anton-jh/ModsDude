using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Tests.GameProcesses;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>Checking a held savegame in, and discarding it.</summary>
public class SavegameCheckInTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _client = SavegameHarness.Client;


    [Fact]
    public async Task A_slot_the_game_saved_to_during_the_check_in_is_not_recycled()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        harness.Uploader.DuringUpload = () => harness.WriteSlotFile(_slot1, "a savegame, played once and saved again");

        var result = await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(SavegameLocalCopy.ChangedSinceUpload, result.LocalCopy);
        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal("a savegame, played once and saved again", harness.ReadSlotFile(_slot1));
    }

    /// <summary>
    /// <b>The whole point of addressing the blob by its content.</b> A night that changed nothing must
    /// not cost a 400 MB upload, and the server says so by answering the upload link request with
    /// <c>alreadyStored</c>.
    /// </summary>
    [Fact]
    public async Task Checking_in_unchanged_bytes_skips_the_upload_entirely()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        var snapshotsBefore = harness.Server.Snapshots.Count;

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(1, harness.Server.UploadLinksMinted);
        Assert.Equal(0, harness.Uploader.Uploads);

        // And the server minted nothing either: a save that changes nothing costs no line of history.
        Assert.Equal(snapshotsBefore, harness.Server.Snapshots.Count);
    }

    [Fact]
    public async Task Checking_in_played_bytes_uploads_them_and_mints_a_snapshot_based_on_what_was_held()
    {
        using var harness = new SavegameHarness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var (snapshot, _, _, _) = await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, "after playing", keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.Equal(head.Number + 1, snapshot.Number);
        Assert.Equal("after playing", snapshot.Label);

        // Based on the snapshot that was actually in the slot, which is the mechanical half of the
        // one-holder-at-a-time guarantee - the checkout is only the social half.
        Assert.Equal(head.Number, Assert.Single(harness.Server.CheckIns).BasedOn);

        // And the revision the folder is actually on, from the manifest.
        Assert.Equal(harness.AppliedRevision, harness.Server.CheckIns[0].ProfileRevision);
    }

    /// <summary>
    /// Play in compatibility mode happened on the old revision, so that is what the snapshot records -
    /// which keeps the save old, and the next check-out asks again. Keeping it checked out keeps the pin.
    /// </summary>
    [Fact]
    public async Task Checking_in_from_compatibility_mode_records_the_pinned_revision_and_keeps_the_pin()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Compatibility, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: true, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).TargetRevision);
    }

    /// <summary>
    /// The three slow things a check-in does, in the order it does them, each with the bytes it is
    /// moving - which is what the strip's bar is drawn from. Without them the whole operation is one
    /// indeterminate bar for as long as a 400 MB save takes to pack and send.
    /// </summary>
    [Fact]
    public async Task Checking_in_reports_packing_then_uploading_then_recording()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var reports = new List<SavegameProgress>();

        await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None,
            new InlineProgress<SavegameProgress>(reports.Add));

        Assert.Equal(
            [SavegameStage.Packing, SavegameStage.Uploading, SavegameStage.Recording],
            reports.Select(x => x.Stage).Distinct());

        var uploading = reports.Where(x => x.Stage is SavegameStage.Uploading).ToList();

        Assert.True(uploading.Last().Total > 0);
        Assert.Equal(uploading.Last().Total, uploading.Last().Completed);
    }

    /// <summary>
    /// <b>Only after the upload is verified</b>, which here means after the commit: a blob no snapshot
    /// names is unreachable, so the upload alone is not the moment.
    /// </summary>
    [Fact]
    public async Task Checking_in_recycles_the_local_copy_only_after_the_commit()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
        Assert.False(Directory.Exists(harness.SlotPath(_slot1)));

        // The slot is free again, which is what removes any need for eviction machinery.
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId));
        Assert.Equal(SavegameSlotAvailability.Free, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// The other half of the same rule. A refused check-in must leave the evening exactly where it
    /// was: recycling on the way out would destroy the only copy of play the server just refused.
    /// </summary>
    [Fact]
    public async Task A_refused_check_in_recycles_nothing_and_keeps_the_binding()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        // Somebody took the save over and checked in while this machine was playing.
        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("somebody else's evening"));

        var exception = await Assert.ThrowsAsync<ApiException<CustomProblemDetails>>(
            () => harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None));

        // Surfaced, never swallowed: forcing past a moved head is a decision only the person holding
        // the save can make, and the caller can only offer it if it can tell this failure apart.
        Assert.Equal(ProblemType.SavegameSnapshotStale, exception.Result.Type);

        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));
        Assert.NotNull(harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId));
    }

    [Fact]
    public async Task Forcing_past_a_moved_head_checks_in_and_records_the_fork()
    {
        using var harness = new SavegameHarness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("somebody else's evening"));

        var (snapshot, _, _, _) = await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: true, takeOver: false, CancellationToken.None);

        Assert.Equal(SavegameSnapshotOrigin.Forced, snapshot.Origin);
        Assert.Equal(head.Number, snapshot.BaseSnapshot);
    }

    /// <summary>
    /// For somebody who wants tonight's progress on the server and intends to carry on. The binding
    /// has to be rebased, or the next check-in is based on a snapshot that is no longer the head and is
    /// refused for a takeover that never happened.
    /// </summary>
    [Fact]
    public async Task Checking_in_and_carrying_on_keeps_the_binding_and_rebases_it()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var (snapshot, _, _, _) = await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: true, force: false, takeOver: false, CancellationToken.None);

        var binding = harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId);

        Assert.NotNull(binding);
        Assert.Equal(snapshot.Number, binding.Value.Snapshot);
        Assert.Equal(snapshot.ContentHash, binding.Value.ContentHash);

        // Nothing was recycled, and the slot still reads as held and clean - which is exactly what
        // "carry on playing" has to mean.
        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));

        // And a second check-in is based on the first, not on the snapshot that was checked out.
        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: true, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(snapshot.Number, harness.Server.CheckIns[^1].BasedOn);
    }

    [Fact]
    public async Task Checking_in_a_savegame_this_machine_does_not_hold_is_refused_with_a_sentence()
    {
        using var harness = new SavegameHarness();

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.CheckIn.CheckInAsync(harness.Game, Guid.NewGuid(), null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None));

        Assert.Contains("not holding", exception.UserMessage);
    }

    /// <summary>
    /// Taken by mistake, never played. Without this the only ways out are a junk snapshot and waiting
    /// to be taken over.
    /// </summary>
    [Fact]
    public async Task Discarding_ends_the_checkout_and_mints_no_snapshot()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        var snapshotsBefore = harness.Server.Snapshots.Count;

        Assert.True(await harness.CheckIn.DiscardAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));

        Assert.Equal(1, harness.Server.CheckoutsDiscarded);
        Assert.Equal(snapshotsBefore, harness.Server.Snapshots.Count);
        Assert.Empty(harness.Server.CheckIns);

        // Nothing was uploaded either - a discard is not a check-in with a shrug.
        Assert.Equal(0, harness.Uploader.Uploads);

        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId));
        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
    }

    [Fact]
    public async Task Checking_in_says_so_when_the_recycle_bin_refuses_the_local_copy()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        harness.RecycleBin.Refuses = true;

        var result = await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(SavegameLocalCopy.LeftBehind, result.LocalCopy);
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId));
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));
    }

    [Fact]
    public async Task Discarding_says_so_when_the_recycle_bin_refuses_the_local_copy()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.RecycleBin.Refuses = true;

        Assert.False(await harness.CheckIn.DiscardAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));

        Assert.Equal(1, harness.Server.CheckoutsDiscarded);
        Assert.Null(harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId));
        Assert.True(Directory.Exists(harness.SlotPath(_slot1)));
    }

    /// <summary>
    /// The same fact seen from the check-in: the fallback for a savegame nobody has played yet is the
    /// revision of <em>the folder it is sitting in</em>, read off that target's own manifest. Two
    /// folders of one game are routinely on different revisions between two applies, and the other
    /// one's number is a mod list this save has never run on.
    /// </summary>
    [Fact]
    public async Task A_check_in_falls_back_to_the_revision_of_the_folder_the_save_is_in()
    {
        using var harness = new SavegameHarness(appliedRevision: 1004);

        harness.AddSecondTarget();
        harness.WriteManifest(harness.ProfileId, 4, "client");

        // Checked out against revision 2, so the binding's own number is 2 - which is what a
        // check-in falls back to when no manifest can answer. Asserting 4 below is therefore
        // asserting that the client folder's manifest was the one read, rather than the server
        // folder's 1004 or nothing at all.
        await harness.SeedHeadAsync("a savegame", profileRevision: 2);
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _client, SavegameRevisionMode.Latest, CancellationToken.None);

        await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    [Fact]
    public async Task Checking_in_with_a_name_writes_it_into_the_slot_before_packing()
    {
        using var harness = new SavegameHarness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        var (snapshot, _, _, _) = await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None,
            savegameName: "New name");

        Assert.Equal("New name", Assert.Single(harness.Adapter.Renames).Name);

        // The name is bytes like any other - writing it is what makes this check-in mint something at
        // all, where an untouched slot would have skipped the upload entirely.
        Assert.NotEqual(head.Number, snapshot.Number);
    }

    /// <summary>
    /// A rename with nothing else in scope - null is left alone rather than asked for.
    /// </summary>
    [Fact]
    public async Task Checking_in_with_no_name_leaves_the_slot_unrenamed()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Empty(harness.Adapter.Renames);
    }

    /// <summary>
    /// A rename edits bytes in the slot exactly as an evening does, and an adapter that could not write
    /// one - the game holding the career file open, say - must not turn that into a failed check-in.
    /// See <see cref="ModsDude.Client.Core.GameAdapters.ILocalSavegameAdapter.RenameSavegame"/>.
    /// </summary>
    [Fact]
    public async Task A_rename_that_fails_does_not_fail_the_check_in()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.Adapter.ThrowOnRename = true;

        var (snapshot, _, _, _) = await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None,
            savegameName: "New name");

        Assert.Single(harness.Adapter.Renames);
        Assert.NotNull(snapshot);
    }

    /// <summary>
    /// <b>The false attribution a rename must never cause.</b> Renaming a save necessarily edits the
    /// bytes beside it, and a naive check-in would read that edit exactly as it reads an evening played:
    /// a hash that no longer matches what was last observed. Here nothing at all is played - the slot
    /// is checked out and immediately checked back in under a new name - so any revision recorded has
    /// to be the fallback for unplayed play, never one implied by the rename.
    /// </summary>
    [Fact]
    public async Task Renaming_a_savegame_nobody_played_does_not_attribute_play_to_it()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        Assert.Null(harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None,
            savegameName: "Renamed, never played");

        // Still nothing played - the rename is the only edit there was - so the revision sent is the
        // ordinary "never played" fallback: the folder's own, not one borrowed from the rename.
        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// <b>The same false attribution, with a real evening in the mix.</b> Reproduces
    /// <see cref="Play_either_side_of_an_apply_is_attributed_to_the_revision_it_ran_on"/>'s first half -
    /// an evening on revision 4, then the profile's head moves to 1004 - but nothing is played after the
    /// apply; only a rename happens at check-in. A check-in that let the rename reach the same
    /// observation an evening does would overwrite the real attribution with 1004, the revision the
    /// folder happens to be on now rather than the one anybody actually played on.
    /// </summary>
    [Fact]
    public async Task Renaming_at_check_in_does_not_overwrite_a_real_evenings_attribution()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        await harness.ApplyAsync(1004);

        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        // Only the name changes from here on - nobody played anything else.
        await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None,
            savegameName: "Renamed after the apply");

        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// A savegame that follows no mod list has nothing a rename could misattribute play to - Observe()
    /// is inert for it regardless - so the rename still happens and the check-in still succeeds.
    /// </summary>
    [Fact]
    public async Task Renaming_a_savegame_with_no_profile_still_renames_it()
    {
        using var harness = new SavegameHarness();
        harness.Server.FollowNoProfile();
        await harness.SeedHeadAsync("a savegame", profileRevision: null);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        await harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None,
            savegameName: "Renamed, no profile");

        Assert.Equal("Renamed, no profile", Assert.Single(harness.Adapter.Renames).Name);
    }

    [Fact]
    public async Task Checking_in_is_refused_while_the_game_is_running()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);
        harness.WriteSlotFile(_slot1, "played");
        harness.Guard.Running = true;

        await Assert.ThrowsAsync<GameRunningException>(
            () => harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None));

        Assert.Empty(harness.Server.CheckIns);
    }

    [Fact]
    public async Task Discarding_is_refused_while_the_game_is_running()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);
        harness.Guard.Running = true;

        await Assert.ThrowsAsync<GameRunningException>(
            () => harness.CheckIn.DiscardAsync(harness.Game, harness.Server.SavegameId, CancellationToken.None));

        Assert.Equal(0, harness.Server.CheckoutsDiscarded);
        Assert.Equal("a savegame", harness.ReadSlotFile(_slot1));
    }
}
