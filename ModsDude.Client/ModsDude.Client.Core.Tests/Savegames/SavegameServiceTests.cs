using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The four verbs, end to end against a real packer on a real disk and a fake server that reproduces
/// the server's actual rules.
/// </summary>
/// <remarks>
/// The packer is real rather than mocked because most of what is being asserted here is only true if
/// packing round-trips: a check-in that skips the upload does so because the bytes it packed hash to
/// what is already stored, and a slot that reads as clean after a check-out does so because unpacking
/// and repacking produce the same archive. A mocked packer would agree with whatever the test
/// assumed, which is precisely the assumption worth checking.
/// </remarks>
public class SavegameServiceTests
{
    private static readonly SavegameSlotId _slot1 = new("savegame1");
    private static readonly SavegameSlotId _slot2 = new("savegame2");


    [Fact]
    public async Task Checking_out_takes_the_claim_writes_the_slot_and_records_what_it_wrote()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal("a savegame", harness.ReadSlotFile(_slot1));

        var binding = harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId);

        Assert.NotNull(binding);
        Assert.Equal(_slot1.Value, binding.Value.SlotId);
        Assert.Equal(head.Number, binding.Value.Version);
        Assert.Equal(head.ContentHash, binding.Value.ContentHash);

        // The two facts the third drift state needs, and the only place they can be recorded: asking
        // the server which revision a held version was played on is a network call in a check that
        // has to work offline.
        Assert.Equal(head.ProfileId, binding.Value.ProfileId);
        Assert.Equal(head.ProfileRevision, binding.Value.ProfileRevision);

        // And the attribution starts from a clean slate: the bytes just written are the boundary the
        // first observation measures from, and nobody has played on anything yet.
        Assert.Equal(head.ContentHash, binding.Value.LastObservedHash);
        Assert.Null(binding.Value.LastPlayedRevision);
    }

    /// <summary>
    /// The refusal that is the point of the whole safety check. The slot holds an evening that exists
    /// nowhere else, and the remedy is to check that savegame in - which is an action, not a warning.
    /// </summary>
    /// <remarks>
    /// The savegame being taken follows no mod list, so the folder limit has nothing to say about it
    /// and this is the slot check refusing on its own. A second one that <em>did</em> claim the folder
    /// would be refused a step earlier, for a different reason - see the test below.
    /// </remarks>
    [Fact]
    public async Task Checking_out_over_unpublished_play_is_refused_before_the_claim_is_taken()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        // An evening in the slot: the contents no longer hash to what was written there.
        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var other = harness.Server.Savegame with
        {
            Id = Guid.NewGuid(),
            Name = "Season 5",
            ProfileId = null,
            Head = head with { ProfileId = null, ProfileRevision = null }
        };

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.CheckOutAsync(harness.Instance, other, _slot1, CancellationToken.None));

        Assert.Contains("nobody has checked in", exception.UserMessage);

        // The destructive step is local and comes first, so nothing was claimed on anybody's behalf
        // and the slot still holds the evening.
        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));
    }

    /// <summary>
    /// A current savegame follows its profile, so it pins the mod folder to nothing and the apply that
    /// comes after the check-out installs head. The head version's revision is emphatically not the
    /// answer: it names the last list this savegame was <em>played</em> on, which is older than head
    /// whenever anybody has edited the profile since - which is the ordinary case, since preparing the
    /// mod list and then checking the savegame out is how a session starts.
    /// </summary>
    [Fact]
    public async Task Checking_out_the_profiles_current_savegame_pins_the_mod_folder_to_nothing()
    {
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Null(harness.Binding(harness.Server.SavegameId).TargetRevision);
        Assert.Null(harness.Service.GetRequiredRevision(harness.Instance.Id, harness.ProfileId));
    }

    /// <summary>
    /// A past savegame's revision does not move, so checking one out is what makes its instance hold a
    /// mod folder pinned to that revision. Recorded on the binding rather than worked out later:
    /// asking the server whether this is still its profile's current savegame is a network call in an apply
    /// rule and a drift check that both have to work offline.
    /// </summary>
    [Fact]
    public async Task Checking_out_a_past_savegame_pins_the_mod_folder_to_its_own_revision()
    {
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).TargetRevision);
        Assert.Equal(4, harness.Service.GetRequiredRevision(harness.Instance.Id, harness.ProfileId));

        // And the apply table now says head is not on offer for this instance.
        Assert.Equal(
            SavegameApplyRefusal.PastSavegameIsHeld,
            harness.Service.DecideApply(harness.Instance.Id, harness.ProfileId, 1004).Refusal);
    }

    /// <summary>
    /// Publishing to a profile makes the new savegame its current one - superseding whatever was - so it
    /// follows the profile from then on rather than staying on the revision it was published at.
    /// </summary>
    [Fact]
    public async Task Publishing_leaves_the_mod_folder_pinned_to_nothing()
    {
        using var harness = new Harness(appliedRevision: 4);

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        var savegame = await harness.Service.PublishAsync(
            harness.Instance, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(), CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.Publishes).ProfileRevision);
        Assert.Null(harness.Binding(savegame.Id).TargetRevision);
    }

    /// <summary>
    /// One mod folder can only be on one revision, so two savegames following two mod lists cannot both be
    /// played out of one instance. Refused before the claim and before the slot is even looked at:
    /// this costs a list read, and a claim taken for a check-out that then refuses itself is one
    /// somebody has to discard by hand.
    /// </summary>
    [Fact]
    public async Task A_second_savegame_that_claims_the_mod_folder_is_refused()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var other = harness.Server.Savegame with { Id = Guid.NewGuid(), Name = "Season 5" };

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.CheckOutAsync(harness.Instance, other, _slot2, CancellationToken.None));

        Assert.Contains("already holding a savegame", exception.UserMessage);

        // The free slot is still free, and nothing was claimed.
        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal(SavegameSlotAvailability.Free, await harness.Service.ClassifySlotAsync(harness.Instance, _slot2, CancellationToken.None));
    }

    /// <summary>
    /// The same limit reached from the other side rather than a rule of its own: a publish opens a
    /// claim in the same transaction as the savegame, so publishing to a profile would leave this
    /// instance holding two savegames that both want its mod folder.
    /// </summary>
    [Fact]
    public async Task Publishing_to_a_profile_while_a_savegame_is_held_is_refused()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot2, "a brand new savegame");

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.PublishAsync(
                harness.Instance, harness.Server.RepoId, _slot2, "Season 5", null, harness.Target(), CancellationToken.None));

        Assert.Contains("already holding a savegame", exception.UserMessage);

        // Refused before the bytes were packed, so nothing was uploaded and no orphan blob was left
        // for the reclamation sweep.
        Assert.Empty(harness.Server.Publishes);
        Assert.Equal(0, harness.Uploader.Uploads);
    }

    /// <summary>
    /// The limit counts mod lists, not savegames. A savegame following none makes no claim on the
    /// folder and cannot conflict with anything, so any number may be held alongside - which is also
    /// what makes the limit vacuous in a repo whose adapter has no mods, with no capability check
    /// anywhere.
    /// </summary>
    [Fact]
    public async Task A_savegame_with_no_profile_may_be_held_beside_one_that_has_one()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var unmanaged = harness.Server.Savegame with
        {
            Id = Guid.NewGuid(),
            Name = "A save of my own",
            ProfileId = null,
            Head = head with { ProfileId = null, ProfileRevision = null }
        };

        await harness.Service.CheckOutAsync(harness.Instance, unmanaged, _slot2, CancellationToken.None);

        Assert.Equal(2, harness.Service.GetBindings(harness.Instance).Count);
    }

    [Fact]
    public async Task A_free_slot_and_an_unrecognised_one_are_told_apart()
    {
        using var harness = new Harness();

        Assert.Equal(SavegameSlotAvailability.Free, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));

        harness.WriteSlotFile(_slot1, "somebody's own savegame");

        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// A check-out followed by nothing leaves the slot holding exactly what was written, which has to
    /// read as clean - if it did not, every check-out would immediately report unpublished play and
    /// the notice would be worthless.
    /// </summary>
    [Fact]
    public async Task A_slot_just_checked_out_into_reads_as_held_and_clean()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task The_picker_pre_selects_the_remembered_slot_and_falls_back_to_the_first_free_one()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        // Nothing remembered yet: the first free slot.
        Assert.Equal(_slot1, await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot2, CancellationToken.None);
        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        // The hint survives the check-in that destroyed the binding - that asymmetry is its whole job.
        Assert.Equal(_slot2, await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));

        // And when the remembered slot is taken by something else, the first free one instead. The
        // hint is left exactly as it was; nothing here repairs it.
        harness.WriteSlotFile(_slot2, "somebody's own savegame");

        Assert.Equal(_slot1, await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));
    }

    [Fact]
    public async Task No_free_slot_pre_selects_nothing()
    {
        using var harness = new Harness();

        harness.WriteSlotFile(_slot1, "one savegame");
        harness.WriteSlotFile(_slot2, "another savegame");

        Assert.Null(await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));
    }

    /// <summary>
    /// <b>The whole point of addressing the blob by its content.</b> A night that changed nothing must
    /// not cost a 400 MB upload, and the server says so by answering the upload link request with
    /// <c>alreadyStored</c>.
    /// </summary>
    [Fact]
    public async Task Checking_in_unchanged_bytes_skips_the_upload_entirely()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var versionsBefore = harness.Server.Versions.Count;

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1, harness.Server.UploadLinksMinted);
        Assert.Equal(0, harness.Uploader.Uploads);

        // And the server minted nothing either: a save that changes nothing costs no line of history.
        Assert.Equal(versionsBefore, harness.Server.Versions.Count);
    }

    [Fact]
    public async Task Checking_in_played_bytes_uploads_them_and_mints_a_version_based_on_what_was_held()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, "after playing", keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.Equal(head.Number + 1, version.Number);
        Assert.Equal("after playing", version.Label);

        // Based on the version that was actually in the slot, which is the mechanical half of the
        // one-holder-at-a-time guarantee - the checkout is only the social half.
        Assert.Equal(head.Number, Assert.Single(harness.Server.CheckIns).BasedOn);

        // And the revision the folder is actually on, from the manifest.
        Assert.Equal(harness.AppliedRevision, harness.Server.CheckIns[0].ProfileRevision);
    }

    /// <summary>
    /// <b>Only after the upload is verified</b>, which here means after the commit: a blob no version
    /// names is unreachable, so the upload alone is not the moment.
    /// </summary>
    [Fact]
    public async Task Checking_in_recycles_the_local_copy_only_after_the_commit()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
        Assert.False(Directory.Exists(harness.SlotPath(_slot1)));

        // The slot is free again, which is what removes any need for eviction machinery.
        Assert.Null(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
        Assert.Equal(SavegameSlotAvailability.Free, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// The other half of the same rule. A refused check-in must leave the evening exactly where it
    /// was: recycling on the way out would destroy the only copy of play the server just refused.
    /// </summary>
    [Fact]
    public async Task A_refused_check_in_recycles_nothing_and_keeps_the_binding()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        // Somebody took the save over and checked in while this machine was playing.
        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("somebody else's evening"));

        var exception = await Assert.ThrowsAsync<ApiException<CustomProblemDetails>>(
            () => harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None));

        // Surfaced, never swallowed: forcing past a moved head is a decision only the person holding
        // the save can make, and the caller can only offer it if it can tell this failure apart.
        Assert.True(SavegameService.IsVersionStale(exception));

        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));
        Assert.NotNull(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
    }

    [Fact]
    public async Task Forcing_past_a_moved_head_checks_in_and_records_the_fork()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("somebody else's evening"));

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: true, CancellationToken.None);

        Assert.Equal(SavegameVersionOrigin.Forced, version.Origin);
        Assert.Equal(head.Number, version.BaseVersion);
    }

    /// <summary>
    /// For somebody who wants tonight's progress on the server and intends to carry on. The binding
    /// has to be rebased, or the next check-in is based on a version that is no longer the head and is
    /// refused for a takeover that never happened.
    /// </summary>
    [Fact]
    public async Task Checking_in_and_carrying_on_keeps_the_binding_and_rebases_it()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: true, force: false, CancellationToken.None);

        var binding = harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId);

        Assert.NotNull(binding);
        Assert.Equal(version.Number, binding.Value.Version);
        Assert.Equal(version.ContentHash, binding.Value.ContentHash);

        // Nothing was recycled, and the slot still reads as held and clean - which is exactly what
        // "carry on playing" has to mean.
        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));

        // And a second check-in is based on the first, not on the version that was checked out.
        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: true, force: false, CancellationToken.None);

        Assert.Equal(version.Number, harness.Server.CheckIns[^1].BasedOn);
    }

    [Fact]
    public async Task Checking_in_a_savegame_this_machine_does_not_hold_is_refused_with_a_sentence()
    {
        using var harness = new Harness();

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.CheckInAsync(harness.Instance, Guid.NewGuid(), null, keepPlaying: false, force: false, CancellationToken.None));

        Assert.Contains("not holding", exception.UserMessage);
    }

    /// <summary>
    /// Taken by mistake, never played. Without this the only ways out are a junk version and waiting
    /// to be taken over.
    /// </summary>
    [Fact]
    public async Task Discarding_ends_the_checkout_and_mints_no_version()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var versionsBefore = harness.Server.Versions.Count;

        await harness.Service.DiscardAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None);

        Assert.Equal(1, harness.Server.CheckoutsDiscarded);
        Assert.Equal(versionsBefore, harness.Server.Versions.Count);
        Assert.Empty(harness.Server.CheckIns);

        // Nothing was uploaded either - a discard is not a check-in with a shrug.
        Assert.Equal(0, harness.Uploader.Uploads);

        Assert.Null(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
    }

    [Fact]
    public async Task Publishing_uploads_the_slot_mints_the_savegame_and_leaves_this_machine_holding_it()
    {
        using var harness = new Harness();

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        var savegame = await harness.Service.PublishAsync(
            harness.Instance, harness.Server.RepoId, _slot1, "Season 5", "the beginning", harness.Target(), CancellationToken.None);

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
        var binding = harness.Service.GetBinding(harness.Instance, savegame.Id);

        Assert.NotNull(binding);
        Assert.Equal(_slot1.Value, binding.Value.SlotId);
        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// A first version's revision is declared rather than observed, so a folder that has never been
    /// synced is not an obstacle: nothing knows which mods were in it while that savegame was played
    /// either way, and requiring a sync first would observe the folder at the moment of publishing -
    /// which is a different fact, not a better one.
    /// </summary>
    [Fact]
    public async Task Publishing_from_an_instance_that_has_never_been_synced_declares_the_profile_head()
    {
        using var harness = new Harness(writeManifest: false);

        harness.WriteSlotFile(_slot1, "a brand new savegame");

        await harness.Service.PublishAsync(
            harness.Instance, harness.Server.RepoId, _slot1, "Season 5", null, harness.Target(headRevision: 7), CancellationToken.None);

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
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        // Already holding one that claims the mod folder, which a publish *to a profile* is refused
        // for. This one claims nothing.
        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot2, "an unmanaged savegame");

        var savegame = await harness.Service.PublishAsync(
            harness.Instance, harness.Server.RepoId, _slot2, "Scratch", null, target: null, CancellationToken.None);

        var request = Assert.Single(harness.Server.Publishes);

        Assert.Null(request.ProfileId);
        Assert.Null(request.ProfileRevision);

        var binding = harness.Binding(savegame.Id);

        Assert.Null(binding.ProfileId);
        Assert.Null(binding.ProfileRevision);
        Assert.Null(binding.TargetRevision);
    }

    /// <summary>
    /// A past savegame made current again follows its profile from here, so the pin that held this
    /// instance's mod folder at revision 4 has to go with it.
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
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).TargetRevision);

        await harness.Service.MakeCurrentAsync([harness.Instance], harness.Server.Savegame, CancellationToken.None);

        Assert.Equal(1, harness.Server.MadeCurrent);
        Assert.Null(harness.Binding(harness.Server.SavegameId).TargetRevision);

        // And with the pin gone, the apply table stops refusing head - which is the whole point of
        // clearing it.
        Assert.True(harness.Service.DecideApply(harness.Instance.Id, harness.ProfileId, 1004).IsAllowed);
    }

    /// <summary>
    /// Nothing here is holding it, which is the ordinary case: the swap is about a savegame, and the
    /// pin is a fact about a mod folder that may be on somebody else's machine entirely.
    /// </summary>
    [Fact]
    public async Task Making_a_savegame_current_touches_no_instance_that_is_not_holding_it()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();

        await harness.Service.MakeCurrentAsync([harness.Instance], harness.Server.Savegame, CancellationToken.None);

        Assert.Equal(1, harness.Server.MadeCurrent);
        Assert.Null(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
    }

    /// <summary>
    /// The revision a first version declares: what the folder is actually on where the chosen profile
    /// is the one it is on, and that profile's head otherwise - which is the honest answer, since the
    /// alternative is a number belonging to a different mod list.
    /// </summary>
    [Fact]
    public void The_declared_revision_is_the_folder_s_where_the_profile_matches_and_head_otherwise()
    {
        var profileId = Guid.NewGuid();

        Assert.Equal(4, SavegameService.DeclaredRevisionFor(profileId, 1004, profileId, 4));
        Assert.Equal(1004, SavegameService.DeclaredRevisionFor(profileId, 1004, Guid.NewGuid(), 4));
        Assert.Equal(1004, SavegameService.DeclaredRevisionFor(profileId, 1004, null, null));
    }

    /// <summary>
    /// What a Guest gets, and what looking at an old version without disturbing anybody looks like:
    /// bytes in a slot, no claim, no binding, and therefore nothing that can be checked in from it.
    /// </summary>
    [Fact]
    public async Task Taking_a_copy_claims_nothing_and_binds_nothing()
    {
        using var harness = new Harness();
        var first = await harness.SeedHeadAsync("a savegame");

        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("a savegame, played once"));

        await harness.Service.TakeCopyAsync(harness.Instance, harness.Server.Savegame, first.Number, _slot1, CancellationToken.None);

        Assert.Equal("a savegame", harness.ReadSlotFile(_slot1));
        Assert.Equal(0, harness.Server.CheckoutsTaken);
        Assert.Empty(harness.Service.GetBindings(harness.Instance));

        // An ordinary unrecognised slot afterwards, which is the honest description: ModsDude has no
        // claim on what is in it and no way to hand it back.
        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task Taking_a_copy_of_a_pruned_version_says_so()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a savegame");

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.TakeCopyAsync(harness.Instance, harness.Server.Savegame, 99, _slot1, CancellationToken.None));

        Assert.Contains("not there any more", exception.UserMessage);
    }


    /// <summary>
    /// <b>The first worked example</b> in docs/10-savegame-profile-binding.md#worked-examples. Checked
    /// out on revision 4, an evening played, the profile applied at 1004 while other people moved it
    /// there, and another evening. The version records 1004, and the evening before the apply was
    /// attributed to 4 as it happened rather than reconstructed afterwards - which is not something
    /// timestamps could have told anybody.
    /// </summary>
    [Fact]
    public async Task Play_either_side_of_an_apply_is_attributed_to_the_revision_it_ran_on()
    {
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        // An evening on revision 4, while a thousand edits move the profile's head to 1004.
        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1004);

        var observed = harness.Binding(harness.Server.SavegameId);

        Assert.Equal(4, observed.LastPlayedRevision);
        Assert.Equal(await harness.HashSlotAsync(_slot1), observed.LastObservedHash);

        // A second evening, this time on the list the folder now runs.
        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1004, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// <b>The second worked example.</b> Same start, but nothing is played after the apply - and a
    /// week goes by. The version records 4, because that is the list the play ran on; the interval
    /// between check-out and check-in does not enter into it, and neither does what the folder is on
    /// at the moment of the hand-back.
    /// </summary>
    [Fact]
    public async Task An_apply_after_the_last_evening_does_not_move_what_that_evening_was_played_on()
    {
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1005);
        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

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
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);
        await harness.ApplyAsync(1004);

        Assert.Null(harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        var versionsBefore = harness.Server.Versions.Count;

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1004, Assert.Single(harness.Server.CheckIns).ProfileRevision);
        Assert.Equal(versionsBefore, harness.Server.Versions.Count);
    }

    /// <summary>
    /// A savegame that follows no mod list takes no part in any of this. It claims no profile, so
    /// there is no revision its play could belong to and none for a check-in to send - and the server
    /// refuses one that sends one anyway.
    /// </summary>
    [Fact]
    public async Task A_savegame_with_no_profile_is_never_attributed_to_a_revision()
    {
        using var harness = new Harness(appliedRevision: 4);

        harness.Server.FollowNoProfile();

        await harness.SeedHeadAsync("a savegame", profileRevision: null);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var bound = harness.Binding(harness.Server.SavegameId);

        Assert.Null(bound.ProfileId);
        Assert.Null(bound.ProfileRevision);

        // Played, and the mod folder moved underneath it - neither of which is any of this savegame's
        // business.
        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1004);

        Assert.Null(harness.Binding(harness.Server.SavegameId).LastPlayedRevision);

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

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
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");
        harness.PointTheFolderAtAnotherProfile(revision: 9);

        await harness.Service.ObserveAsync(harness.Instance.Id, CancellationToken.None);

        var observed = harness.Binding(harness.Server.SavegameId);

        Assert.Null(observed.LastPlayedRevision);
        Assert.Equal(await harness.HashSlotAsync(_slot1), observed.LastObservedHash);

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(4, Assert.Single(harness.Server.CheckIns).ProfileRevision);
    }

    /// <summary>
    /// An apply with nothing to observe writes nothing. A binding rewritten on every apply would save
    /// the whole of local state and wake the drift notice for an answer that has not changed.
    /// </summary>
    [Fact]
    public async Task An_apply_that_finds_no_play_records_nothing()
    {
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

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
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        await harness.ApplyAsync(1004);

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));

        // One kind and not two: the folder is on 1004 and the binding was checked out at 4, which is
        // this savegame following its profile rather than leaving its mod list.
        Assert.Equal(SavegameDriftKind.UncheckedInPlay, drift.Kind);
    }

    /// <summary>
    /// Carrying on playing is a check-out in every respect that matters: the version on the server is
    /// these bytes, so both boundaries move and the next evening is the first that has not been
    /// recorded anywhere. Leaving the old attribution behind would credit tonight's play to the list
    /// last night ran on.
    /// </summary>
    [Fact]
    public async Task Carrying_on_playing_starts_the_attribution_over()
    {
        using var harness = new Harness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: true, force: false, CancellationToken.None);

        var rebased = harness.Binding(harness.Server.SavegameId);

        Assert.Equal(version.ContentHash, rebased.LastObservedHash);
        Assert.Null(rebased.LastPlayedRevision);

        // Tonight's evening happens after the folder moved, and is recorded against where it is now.
        await harness.ApplyAsync(1004);

        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1004, harness.Server.CheckIns[^1].ProfileRevision);
    }


    /// <summary>
    /// A real disk, a real packer and a fake server, wired the way the app wires them.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly TempDirectory _slots = new("savegame-service-slots");
        private readonly TempDirectory _manifests = new("savegame-service-manifests");


        public Harness(bool writeManifest = true, int appliedRevision = 1)
        {
            AppliedRevision = appliedRevision;

            var persisted = new PersistedLocalInstance
            {
                Id = Guid.NewGuid(),
                Scope = new GameIdentity("farmingSimulator", "fs25"),
                GameAdapterId = new GameAdapterId("farmingSimulator", 1),
                Name = "Farming Simulator 25",
                AdapterLocalSettings = "{}",
                ModFolder = _slots.Path,
                ActiveProfile = new ActiveProfile(Server.RepoId, Server.ProfileId)
            };

            State.Add(persisted);
            Instance = new LocalInstance(persisted);

            Uploader = new FakeSavegameUploader(Server);
            Adapter = new FakeSavegameAdapter(_slots.Path, _slot1.Value, _slot2.Value);
            Bindings = new SavegameBindingStore(State);
            ManifestStore = new SyncManifestStore(_manifests.Path);

            if (writeManifest)
            {
                WriteManifest(appliedRevision);
            }

            Service = new SavegameService(
                Server,
                Server,
                new SavegamePacker(),
                Bindings,
                new FakeInstanceSavegameAdapters(Adapter),
                new FakeSavegameDownloader(Server),
                Uploader,
                ManifestStore,
                RecycleBin,
                NullLogger<SavegameService>.Instance,
                Heads);
        }


        /// <summary>Which revision of the profile the mod folder is on, per the manifest.</summary>
        public int AppliedRevision { get; private set; }

        public FakeSavegameServer Server { get; } = new();
        public FakeSavegameUploader Uploader { get; }
        public FakeSlotRecycleBin RecycleBin { get; } = new();
        public FakeSavegameHeadVersions Heads { get; } = new();
        public FakeInstanceState State { get; } = new();
        public FakeSavegameAdapter Adapter { get; }
        public SavegameBindingStore Bindings { get; }
        public SyncManifestStore ManifestStore { get; }
        public SavegameService Service { get; }
        public LocalInstance Instance { get; }

        public Guid ProfileId => Server.ProfileId;


    /// <summary>
    /// What the publish dialog settles: which mod list the new savegame follows, and the revision its
    /// first version declares.
    /// </summary>
    /// <remarks>
    /// Through <see cref="SavegameService.DeclaredRevisionFor"/> rather than by naming a number, so
    /// these tests exercise the rule the dialog shows rather than a second copy of it.
    /// </remarks>
    public SavegamePublishTarget Target(int headRevision = 1)
    {
        var manifest = ManifestStore.TryRead(Instance.Id);

        return new SavegamePublishTarget(
            ProfileId,
            SavegameService.DeclaredRevisionFor(ProfileId, headRevision, manifest?.ProfileId, manifest?.ProfileRevision));
    }


        /// <summary>Puts a savegame on the server whose bytes are a real packed slot.</summary>
        public async Task<SavegameVersionDto> SeedHeadAsync(string content, int? profileRevision = 1)
            => Server.Seed(await PackedBytesAsync(content), profileRevision);

        /// <summary>
        /// What applying a profile does to this instance, in the order the sync engine does it:
        /// attribute whatever has been played on the outgoing revision, then say the folder is on the
        /// incoming one. The mod folder itself is beside the point here - no savegame is in it.
        /// </summary>
        public async Task ApplyAsync(int revision)
        {
            await Service.ObserveAsync(Instance.Id, CancellationToken.None);

            WriteManifest(revision);
        }

        public void WriteManifest(int revision) => WriteManifest(Server.ProfileId, revision);

        /// <summary>
        /// The instance re-pointed at a different mod list and synced to it, which the rules slice
        /// forbids while a savegame is held and which nothing stops today.
        /// </summary>
        public void PointTheFolderAtAnotherProfile(int revision) => WriteManifest(Guid.NewGuid(), revision);

        private void WriteManifest(Guid profileId, int revision)
        {
            AppliedRevision = revision;

            ManifestStore.Write(new SyncManifest
            {
                InstanceId = Instance.Id,
                RepoId = Server.RepoId,
                ProfileId = profileId,
                ProfileRevision = revision,
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = _slots.Path,
                Entries = []
            });
        }

        /// <summary>What this machine records about a savegame it is holding.</summary>
        public SavegameCheckoutBinding Binding(Guid savegameId)
            => Service.GetBinding(Instance, savegameId) ?? throw new InvalidOperationException("Nothing is held.");

        /// <summary>
        /// What a slot holding <paramref name="content"/> packs to. Built through the real packer in a
        /// staging slot, because the archive the server serves has to be one the packer would produce
        /// - otherwise a check-out followed by a check-in would look like play.
        /// </summary>
        public async Task<byte[]> PackedBytesAsync(string content)
        {
            var staging = new SavegameSlotId($"staging-{Guid.NewGuid():N}");

            WriteSlotFile(staging, content);

            var packed = await new SavegamePacker().PackAsync(Adapter, staging, CancellationToken.None);

            try
            {
                return await File.ReadAllBytesAsync(packed.FilePath);
            }
            finally
            {
                File.Delete(packed.FilePath);
                Directory.Delete(SlotPath(staging), recursive: true);
            }
        }

        public string SlotPath(SavegameSlotId slot) => Adapter.GetSlotPath(slot);

        /// <summary>What the packer says a slot holds now - the value an observation compares.</summary>
        public Task<string> HashSlotAsync(SavegameSlotId slot)
            => new SavegamePacker().HashSlotAsync(Adapter, slot, CancellationToken.None);

        public void WriteSlotFile(SavegameSlotId slot, string content)
        {
            var path = Path.Combine(SlotPath(slot), "careerSavegame.xml");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string ReadSlotFile(SavegameSlotId slot)
            => File.ReadAllText(Path.Combine(SlotPath(slot), "careerSavegame.xml"));

        public void Dispose()
        {
            _slots.Dispose();
            _manifests.Dispose();
        }
    }
}
