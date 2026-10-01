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

/// <summary>Checking a savegame out, and taking a copy of a snapshot.</summary>
public class SavegameCheckOutTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;
    private static readonly SavegameSlotRef _slot2 = SavegameHarness.Slot2;
    private static readonly SavegameSlotRef _client = SavegameHarness.Client;


    [Fact]
    public async Task Checking_out_takes_the_claim_writes_the_slot_and_records_what_it_wrote()
    {
        using var harness = new SavegameHarness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal("a savegame", harness.ReadSlotFile(_slot1));

        var binding = harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId);

        Assert.NotNull(binding);
        Assert.Equal(_slot1, binding.Value.Slot);
        Assert.Equal(head.Number, binding.Value.Snapshot);
        Assert.Equal(head.ContentHash, binding.Value.ContentHash);

        // The two facts the third drift state needs, and the only place they can be recorded: asking
        // the server which revision a held snapshot was played on is a network call in a check that
        // has to work offline.
        Assert.Equal(head.ProfileId, binding.Value.ProfileId);
        Assert.Equal(head.ProfileRevision, binding.Value.ProfileRevision);

        // And the attribution starts from a clean slate: the bytes just written are the boundary the
        // first observation measures from, and nobody has played on anything yet.
        Assert.Equal(head.ContentHash, binding.Value.LastObservedHash);
        Assert.Null(binding.Value.LastPlayedRevision);
    }

    /// <summary>
    /// Taking a save from somebody is allowed, and the server says whose it was so the caller can
    /// name them - and until the next list read, the claim just taken is recorded as this user's, so a
    /// drift check in between cannot report the check-out as their own save having been taken over.
    /// </summary>
    [Fact]
    public async Task Checking_out_a_save_somebody_else_holds_says_whose_it_was_and_records_the_claim_as_yours()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        var since = DateTime.UtcNow.AddHours(-3);
        harness.Server.HeldBySomebodyElse = new SavegameCheckoutDto
        {
            Id = Guid.NewGuid(),
            RepoId = harness.Server.RepoId,
            SavegameId = harness.Server.SavegameId,
            User = new UserDto { Id = "bob", DisplayName = "Bob", Tag = "0001" },
            TakenAt = since,
            EndedAt = DateTime.UtcNow,
            EndedReason = SavegameCheckoutEndReason.TakenOver,
            Status = SavegameCheckoutStatus.Ended
        };
        harness.Sightings.SetClaim(harness.Server.SavegameId, new SavegameClaimSighting(
            new SavegameClaimHolder("bob", "Bob", since), IsYours: false));

        var (takenFrom, _) = await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(new SavegameClaimHolder("bob", "Bob", since), takenFrom);
        Assert.True(harness.Sightings.GetClaim(harness.Server.RepoId, harness.Server.SavegameId)?.IsYours);
    }

    [Fact]
    public async Task Checking_out_over_somebodys_own_save_sends_it_to_the_recycle_bin_under_a_readable_name()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        harness.WriteSlotFile(_slot1, "my own save");

        var (_, displaced) = await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(DisplacedSavegameDestination.RecycleBin, displaced?.Destination);
        Assert.StartsWith("savegame1 (replaced ", displaced!.Name);
        Assert.Equal(Path.GetFileName(Assert.Single(harness.RecycleBin.Recycled)), displaced.Name);
        Assert.Equal("a savegame", harness.ReadSlotFile(_slot1));
    }

    [Fact]
    public async Task A_replaced_save_the_recycle_bin_refuses_goes_to_quarantine()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        harness.WriteSlotFile(_slot1, "my own save");
        harness.RecycleBin.Refuses = true;

        var (_, displaced) = await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(DisplacedSavegameDestination.QuarantineFolder, displaced?.Destination);
        Assert.StartsWith(harness.Store.QuarantinePath, displaced!.Path);
        Assert.Equal("my own save", File.ReadAllText(Path.Combine(displaced.Path!, "careerSavegame.xml")));
    }

    [Fact]
    public async Task A_replaced_save_nothing_will_take_stays_beside_the_slots()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        harness.WriteSlotFile(_slot1, "my own save");
        harness.RecycleBin.Refuses = true;
        File.WriteAllText(harness.Store.QuarantinePath, "a file where the quarantine folder would go");

        var displaced = await harness.CheckOut.TakeCopyAsync(harness.Game, harness.Server.Savegame, 1, _slot1, CancellationToken.None);

        Assert.Equal(DisplacedSavegameDestination.BesideSlots, displaced?.Destination);
        Assert.Equal(Path.GetDirectoryName(harness.SlotPath(_slot1)), Path.GetDirectoryName(displaced!.Path));
        Assert.Equal("my own save", File.ReadAllText(Path.Combine(displaced.Path!, "careerSavegame.xml")));
    }

    [Fact]
    public async Task Checking_out_a_save_nobody_holds_took_it_from_nobody()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        Assert.Null((await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None)).TakenFrom);
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
        using var harness = new SavegameHarness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

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
            () => harness.CheckOut.CheckOutAsync(harness.Game, other, _slot1, CancellationToken.None));

        Assert.Contains("nobody has checked in", exception.UserMessage);

        // The destructive step is local and comes first, so nothing was claimed on anybody's behalf
        // and the slot still holds the evening.
        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));
    }

    /// <summary>
    /// A current savegame follows its profile, so it pins the mod folder to nothing and the apply that
    /// comes after the check-out installs head. The head snapshot's revision is emphatically not the
    /// answer: it names the last list this savegame was <em>played</em> on, which is older than head
    /// whenever anybody has edited the profile since - which is the ordinary case, since preparing the
    /// mod list and then checking the savegame out is how a session starts.
    /// </summary>
    [Fact]
    public async Task Checking_out_the_profiles_current_savegame_pins_the_mod_folder_to_nothing()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Null(harness.Binding(harness.Server.SavegameId).TargetRevision);
        Assert.Null(harness.HeldSavegames.GetRequiredRevision(harness.Game.Identity, harness.ProfileId));
    }

    /// <summary>
    /// A past savegame's revision does not move, so checking one out is what makes its game hold a
    /// mod folder pinned to that revision. Recorded on the binding rather than worked out later:
    /// asking the server whether this is still its profile's current savegame is a network call in an apply
    /// rule and a drift check that both have to work offline.
    /// </summary>
    [Fact]
    public async Task Checking_out_a_past_savegame_pins_the_mod_folder_to_its_own_revision()
    {
        using var harness = new SavegameHarness(appliedRevision: 4);
        await harness.SeedHeadAsync("a savegame", profileRevision: 4);

        harness.Server.Supersede();

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(4, harness.Binding(harness.Server.SavegameId).TargetRevision);
        Assert.Equal(4, harness.HeldSavegames.GetRequiredRevision(harness.Game.Identity, harness.ProfileId));

        // And the apply table now says head is not on offer for this game.
        Assert.Equal(
            SavegameApplyRefusal.PastSavegameIsHeld,
            harness.HeldSavegames.DecideApply(harness.Game.Identity, harness.ProfileId, 1004).Refusal);
    }

    /// <summary>
    /// One mod folder can only be on one revision, so two savegames following two mod lists cannot both be
    /// played out of one game. Refused before the claim and before the slot is even looked at:
    /// this costs a list read, and a claim taken for a check-out that then refuses itself is one
    /// somebody has to discard by hand.
    /// </summary>
    [Fact]
    public async Task A_second_savegame_that_claims_the_mod_folder_is_refused()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        var other = harness.Server.Savegame with { Id = Guid.NewGuid(), Name = "Season 5" };

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.CheckOut.CheckOutAsync(harness.Game, other, _slot2, CancellationToken.None));

        Assert.Contains("already holding a savegame", exception.UserMessage);

        // The free slot is still free, and nothing was claimed.
        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal(SavegameSlotAvailability.Free, await harness.Slots.ClassifySlotAsync(harness.Game, _slot2, CancellationToken.None));
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
        using var harness = new SavegameHarness();
        var head = await harness.SeedHeadAsync("a savegame");

        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        var unmanaged = harness.Server.Savegame with
        {
            Id = Guid.NewGuid(),
            Name = "A save of my own",
            ProfileId = null,
            Head = head with { ProfileId = null, ProfileRevision = null }
        };

        await harness.CheckOut.CheckOutAsync(harness.Game, unmanaged, _slot2, CancellationToken.None);

        Assert.Equal(2, harness.Bindings.GetBindings(harness.Game.Identity).Count);
    }

    [Fact]
    public async Task Checking_out_reports_downloading_then_verifying_then_unpacking()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        var reports = new List<SavegameProgress>();

        await harness.CheckOut.CheckOutAsync(
            harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None,
            new InlineProgress<SavegameProgress>(reports.Add));

        Assert.Equal(
            [SavegameStage.Downloading, SavegameStage.Verifying, SavegameStage.Unpacking],
            reports.Select(x => x.Stage).Distinct());

        // Each stage ends where it said it was going.
        foreach (var stage in reports.Select(x => x.Stage).Distinct())
        {
            var last = reports.Last(x => x.Stage == stage);

            Assert.Equal(last.Total, last.Completed);
        }
    }

    /// <summary>
    /// What a Guest gets, and what looking at an old snapshot without disturbing anybody looks like:
    /// bytes in a slot, no claim, no binding, and therefore nothing that can be checked in from it.
    /// </summary>
    [Fact]
    public async Task Taking_a_copy_claims_nothing_and_binds_nothing()
    {
        using var harness = new SavegameHarness();
        var first = await harness.SeedHeadAsync("a savegame");

        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("a savegame, played once"));

        await harness.CheckOut.TakeCopyAsync(harness.Game, harness.Server.Savegame, first.Number, _slot1, CancellationToken.None);

        Assert.Equal("a savegame", harness.ReadSlotFile(_slot1));
        Assert.Equal(0, harness.Server.CheckoutsTaken);
        Assert.Empty(harness.Bindings.GetBindings(harness.Game.Identity));

        // An ordinary unrecognised slot afterwards, which is the honest description: ModsDude has no
        // claim on what is in it and no way to hand it back.
        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task Taking_a_copy_of_a_pruned_snapshot_says_so()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.CheckOut.TakeCopyAsync(harness.Game, harness.Server.Savegame, 99, _slot1, CancellationToken.None));

        Assert.Contains("not there any more", exception.UserMessage);
    }

    /// <summary>
    /// <b>The hold limit counts per game, not per folder.</b> You play one save at a time; hosting one
    /// on the dedicated server while playing another in singleplayer would hold two of the group's
    /// saves and block two people - and the two folders would have to be on two mod lists to do it,
    /// which one profile per game refuses anyway.
    /// </summary>
    [Fact]
    public async Task A_savegame_held_in_one_folder_stops_another_being_taken_in_the_other()
    {
        using var harness = new SavegameHarness();

        harness.AddSecondTarget();

        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None);

        var other = harness.Server.Savegame with { Id = Guid.NewGuid(), Name = "Season 5" };

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.CheckOut.CheckOutAsync(harness.Game, other, _client, CancellationToken.None));

        Assert.Contains("already holding a savegame", exception.UserMessage);
        Assert.Equal(1, harness.Server.CheckoutsTaken);
    }

    [Fact]
    public async Task Checking_out_is_refused_while_the_game_is_running_and_the_slot_is_left_alone()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        harness.WriteSlotFile(_slot1, "my own save");
        harness.Guard.Running = true;

        await Assert.ThrowsAsync<GameRunningException>(
            () => harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, CancellationToken.None));

        Assert.Equal("my own save", harness.ReadSlotFile(_slot1));
        Assert.Equal(0, harness.Server.CheckoutsTaken);
    }

    [Fact]
    public async Task Taking_a_copy_is_refused_while_the_game_is_running()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        harness.WriteSlotFile(_slot1, "my own save");
        harness.Guard.Running = true;

        await Assert.ThrowsAsync<GameRunningException>(
            () => harness.CheckOut.TakeCopyAsync(harness.Game, harness.Server.Savegame, 1, _slot1, CancellationToken.None));

        Assert.Equal("my own save", harness.ReadSlotFile(_slot1));
    }
}
