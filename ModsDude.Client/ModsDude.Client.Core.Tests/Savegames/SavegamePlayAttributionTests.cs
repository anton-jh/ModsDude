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

/// <summary>Which revision play on a held savegame is attributed to.</summary>
public class SavegamePlayAttributionTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _client = SavegameHarness.Client;


    /// <summary>
    /// <b>The first worked example</b> in docs/10-savegame-profile-binding.md#worked-examples. Checked
    /// out on revision 4, an evening played, the profile applied at 1004 while other people moved it
    /// there, and another evening. The snapshot records 1004, and the evening before the apply was
    /// attributed to 4 as it happened rather than reconstructed afterwards - which is not something
    /// timestamps could have told anybody.
    /// </summary>
    [Fact]
    public async Task Play_either_side_of_an_apply_is_attributed_to_the_revision_it_ran_on()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        // An evening on revision 4, while a thousand edits move the profile's head to 1004.
        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1004);

        var observed = harness.Binding(harness.Server.SavegameId);

        Assert.Equal(4, observed.LastPlayedRevision);
        Assert.Equal(await harness.HashSlotAsync(_slot1), observed.LastObservedHash);

        // A second evening, this time on the list the folder now runs.
        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(1004, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// <b>The second worked example.</b> Same start, but nothing is played after the apply - and a
    /// week goes by. The snapshot records 4, because that is the list the play ran on; the interval
    /// between check-out and check-in does not enter into it, and neither does what the folder is on
    /// at the moment of the hand-back.
    /// </summary>
    [Fact]
    public async Task An_apply_after_the_last_evening_does_not_move_what_that_evening_was_played_on()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1005);
        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// Nothing was ever played, so there is nothing to attribute and the folder's own revision is what
    /// the check-in names. It costs no line of history either: the slot's bytes are still the head's,
    /// so the server mints nothing.
    /// </summary>
    [Fact]
    public async Task A_savegame_that_was_never_played_records_the_revision_the_folder_is_on_now()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);
        await harness.ApplyAsync(1004);

        Assert.Null(harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        var snapshotsBefore = harness.Server.Snapshots.Count;

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(1004, Assert.Single(harness.Server.CheckIns).ProfileRevision);
        Assert.Equal(snapshotsBefore, harness.Server.Snapshots.Count);
    }

    /// <summary>
    /// <b>The BeamMP evening, and the reason attribution is per folder.</b> A save is being played in
    /// the MP client's folder while the dedicated server's mod list moves; the play happened in the
    /// client's folder and against the client's mods, so the server's apply has nothing to attribute
    /// and must not say it does. Attributing per game would credit an evening to a revision that was
    /// installed somewhere the save has never been.
    /// </summary>
    [Fact]
    public async Task An_apply_to_one_folder_does_not_attribute_the_play_held_in_another()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);

        harness.AddSecondTarget();
        harness.WriteManifest(harness.ProfileId, 4, "client");

        await harness.SeedHeadAsync("a savegame", profileRevision: 4);
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _client, SavegameRevisionMode.Latest, CancellationToken.None);

        // An evening in the MP client's folder.
        harness.WriteSlotFile(_client, "a savegame, played once");

        // The dedicated server is applied to, and moves to 1004. Nothing that happened in the client's
        // folder belongs to that number.
        await harness.ApplyAsync(1004);

        Assert.Null(harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        // Then the client folder gets the same apply. Its outgoing revision is 4, which is what the
        // evening actually ran on - and it is that folder's manifest that says so.
        await harness.ApplyAsync(1004, "client");

        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).LastPlayedRevision);
    }

    /// <summary>
    /// A savegame that follows no mod list takes no part in any of this. It claims no profile, so
    /// there is no revision its play could belong to and none for a check-in to send - and the server
    /// refuses one that sends one anyway.
    /// </summary>
    [Fact]
    public async Task A_savegame_with_no_profile_is_never_attributed_to_a_revision()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);

        harness.Server.FollowNoProfile();

        await harness.SeedHeadAsync("a savegame", profileRevision: null);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        var bound = harness.Binding(harness.Server.SavegameId);

        Assert.Null(bound.ProfileId);
        Assert.Null(bound.ProfileRevision);

        // Played, and the mod folder moved underneath it - neither of which is any of this savegame's
        // business.
        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1004);

        Assert.Null(harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Null(Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// Revision 6 of one mod list and revision 6 of another are different lists that happen to share
    /// an integer, so a folder pointed at some other profile has no number this savegame can record -
    /// and the server would refuse it as not being this savegame's profile's. The play is still seen,
    /// and the check-in falls back to the list the save was handed over on.
    /// </summary>
    [Fact]
    public async Task Play_on_a_folder_that_belongs_to_another_profile_is_attributed_to_no_revision()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        harness.PointTheFolderAtAnotherProfile(revision: 9);

        await harness.PlayAttribution.ObserveAsync(Keys.Target(), CancellationToken.None);

        var observed = harness.Binding(harness.Server.SavegameId);

        Assert.Null(observed.LastPlayedRevision);
        Assert.Equal(await harness.HashSlotAsync(_slot1), observed.LastObservedHash);

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// An apply with nothing to observe writes nothing. A binding rewritten on every apply would save
    /// the whole of local state and wake the drift notice for an answer that has not changed.
    /// </summary>
    [Fact]
    public async Task An_apply_that_finds_no_play_records_nothing()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        var savesBefore = harness.State.Saves;

        await harness.ApplyAsync(1004);
        await harness.ApplyAsync(1006);

        Assert.Equal(savesBefore, harness.State.Saves);
    }

    /// <summary>
    /// The two hashes answer different questions, and an observation must not silence the notice. After
    /// the apply the slot and <c>LastObservedHash</c> are both the played bytes, so nothing further is
    /// attributed - and the check-out hash is still what the server holds, so the evening is still
    /// reported as existing on this disk and nowhere else.
    /// </summary>
    [Fact]
    public async Task An_observation_does_not_stop_the_notice_reporting_play_nobody_has_checked_in()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1004);

        var drift = Assert.Single(await harness.DriftCheck.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));

        // One kind and not two: the folder is on 1004 and the binding was checked out at 4, which is
        // this savegame following its profile rather than leaving its mod list.
        Assert.Equal(SavegameDriftKind.UncheckedInPlay, drift.Kind);
    }

    /// <summary>
    /// Carrying on playing is a check-out in every respect that matters: the snapshot on the server is
    /// these bytes, so both boundaries move and the next evening is the first that has not been
    /// recorded anywhere. Leaving the old attribution behind would credit tonight's play to the list
    /// last night ran on.
    /// </summary>
    [Fact]
    public async Task Carrying_on_playing_starts_the_attribution_over()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var (snapshot, _, _, _) = await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: true, force: false, takeOver: false, CancellationToken.None);

        var rebased = harness.Binding(harness.Server.SavegameId);

        Assert.Equal(snapshot.ContentHash, rebased.LastObservedHash);
        Assert.Null(rebased.LastPlayedRevision);

        // Tonight's evening happens after the folder moved, and is recorded against where it is now.
        await harness.ApplyAsync(1004);

        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await harness.CheckIn.CheckInAsync(harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, CancellationToken.None);

        Assert.Equal(1004, harness.Server.CheckIns[^1].ProfileRevision);
    }
}
