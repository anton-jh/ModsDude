using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameSlotStateTests
{
    [Fact]
    public void An_empty_slot_nothing_was_written_into_is_free()
    {
        var state = SavegameSlotStates.Classify(Slot("savegame1", occupied: false), null, null);

        Assert.Equal(SavegameSlotAvailability.Free, state);
        Assert.Equal(SavegameSlotWriteDecision.Allowed, SavegameSlotStates.DecideWrite(state));
    }

    /// <summary>
    /// Occupied with no binding is somebody's own save that was never published. ModsDude has no copy
    /// of it anywhere, which is precisely why it is called out rather than treated as spare room.
    /// </summary>
    [Fact]
    public void An_occupied_slot_this_machine_never_checked_out_is_unrecognised()
    {
        var state = SavegameSlotStates.Classify(Slot("savegame1", occupied: true), null, null);

        Assert.Equal(SavegameSlotAvailability.Unrecognised, state);
        Assert.Equal(SavegameSlotWriteDecision.NeedsConfirmation, SavegameSlotStates.DecideWrite(state));
    }

    [Fact]
    public void A_held_slot_still_holding_what_was_written_is_clean()
    {
        var state = SavegameSlotStates.Classify(
            Slot("savegame3", occupied: true),
            Binding("savegame3", hash: "aaaa"),
            currentContentHash: "aaaa");

        Assert.Equal(SavegameSlotAvailability.HeldClean, state);
    }

    /// <summary>
    /// The hex casing is a formatting choice of whoever wrote the value, not part of the hash. A
    /// comparison that disagreed would report unpublished play on every launch, which is the warning
    /// everybody learns to click past.
    /// </summary>
    [Fact]
    public void A_hash_recorded_in_different_casing_is_still_the_same_bytes()
    {
        var state = SavegameSlotStates.Classify(
            Slot("savegame3", occupied: true),
            Binding("savegame3", hash: "ABCD"),
            currentContentHash: "abcd");

        Assert.Equal(SavegameSlotAvailability.HeldClean, state);
    }

    [Fact]
    public void A_held_slot_whose_contents_have_moved_holds_unpublished_play()
    {
        var state = SavegameSlotStates.Classify(
            Slot("savegame3", occupied: true),
            Binding("savegame3", hash: "aaaa"),
            currentContentHash: "bbbb");

        Assert.Equal(SavegameSlotAvailability.HeldWithUnpublishedPlay, state);
    }

    /// <summary>
    /// <b>The rule the whole file exists for.</b> Hashing a savegame folder costs real time, so a
    /// caller is allowed to skip it - and what it gets back is the cautious answer. A false positive
    /// costs one needless prompt; a false negative costs somebody their evening.
    /// </summary>
    [Fact]
    public void A_held_slot_whose_hash_was_not_computed_is_assumed_to_hold_play()
    {
        var state = SavegameSlotStates.Classify(
            Slot("savegame3", occupied: true),
            Binding("savegame3", hash: "aaaa"),
            currentContentHash: null);

        Assert.Equal(SavegameSlotAvailability.HeldWithUnpublishedPlay, state);
    }

    /// <summary>
    /// A binding over an empty slot means the user deleted or moved the folder from inside the game.
    /// There is nothing there to lose, so it is Free rather than Unrecognised - and clearing the
    /// stale binding belongs to the caller that knows whether the savegame is still worth holding,
    /// not to a pure classifier that does not write.
    /// </summary>
    [Fact]
    public void A_binding_whose_slot_has_since_been_emptied_is_free()
    {
        var state = SavegameSlotStates.Classify(
            Slot("savegame3", occupied: false),
            Binding("savegame3", hash: "aaaa"),
            currentContentHash: null);

        Assert.Equal(SavegameSlotAvailability.Free, state);
        Assert.Equal(SavegameSlotWriteDecision.Allowed, SavegameSlotStates.DecideWrite(state));
    }

    /// <summary>
    /// A binding for another slot says nothing about this one. The failure to avoid is using its hash
    /// to declare this slot clean, so the mismatch reduces to "no binding" - which lands on the
    /// cautious side of every remaining branch.
    /// </summary>
    [Fact]
    public void A_binding_naming_a_different_slot_says_nothing_about_this_one()
    {
        var state = SavegameSlotStates.Classify(
            Slot("savegame3", occupied: true),
            Binding("savegame7", hash: "aaaa"),
            currentContentHash: "aaaa");

        Assert.Equal(SavegameSlotAvailability.Unrecognised, state);
    }

    /// <summary>
    /// Slot ids are folder and save names on Windows, where two spellings are one place. Reading them
    /// as two slots would let a write land on top of the save the binding was protecting.
    /// </summary>
    [Fact]
    public void Slot_ids_differing_only_in_casing_are_the_same_slot()
    {
        var state = SavegameSlotStates.Classify(
            Slot("Savegame3", occupied: true),
            Binding("savegame3", hash: "aaaa"),
            currentContentHash: "aaaa");

        Assert.Equal(SavegameSlotAvailability.HeldClean, state);
    }

    /// <summary>
    /// <b>Refused, not warned.</b> That slot holds play that exists nowhere else, and a confirmation
    /// dialog here is a button that destroys it. The remedy is checking that savegame in first, which
    /// is an action rather than a warning.
    /// </summary>
    [Fact]
    public void Writing_over_unpublished_play_is_refused_rather_than_confirmed()
    {
        Assert.Equal(
            SavegameSlotWriteDecision.Refused,
            SavegameSlotStates.DecideWrite(SavegameSlotAvailability.HeldWithUnpublishedPlay));

        Assert.True(SavegameSlotStates.IsRefused(SavegameSlotAvailability.HeldWithUnpublishedPlay));

        // Not a confirmation. A caller that only asked "does this need confirming?" and got true
        // would have prompted its way over somebody's evening.
        Assert.False(SavegameSlotStates.RequiresConfirmation(SavegameSlotAvailability.HeldWithUnpublishedPlay));
    }

    [Theory]
    [InlineData(SavegameSlotAvailability.Free, SavegameSlotWriteDecision.Allowed)]
    [InlineData(SavegameSlotAvailability.HeldClean, SavegameSlotWriteDecision.NeedsConfirmation)]
    [InlineData(SavegameSlotAvailability.Unrecognised, SavegameSlotWriteDecision.NeedsConfirmation)]
    [InlineData(SavegameSlotAvailability.HeldWithUnpublishedPlay, SavegameSlotWriteDecision.Refused)]
    public void Every_state_maps_to_one_decision(SavegameSlotAvailability availability, SavegameSlotWriteDecision expected)
    {
        Assert.Equal(expected, SavegameSlotStates.DecideWrite(availability));
        Assert.Equal(expected is SavegameSlotWriteDecision.NeedsConfirmation, SavegameSlotStates.RequiresConfirmation(availability));
        Assert.Equal(expected is SavegameSlotWriteDecision.Refused, SavegameSlotStates.IsRefused(availability));
    }

    /// <summary>
    /// A free slot is written with no confirmation at all - the ordinary night stays one click. Only
    /// worth a test because it is the case a nervous implementation quietly adds a dialog to.
    /// </summary>
    [Fact]
    public void A_free_slot_is_written_without_asking()
    {
        Assert.False(SavegameSlotStates.RequiresConfirmation(SavegameSlotAvailability.Free));
        Assert.False(SavegameSlotStates.IsRefused(SavegameSlotAvailability.Free));
    }

    [Fact]
    public void The_decision_can_be_taken_straight_from_a_slot()
    {
        Assert.Equal(
            SavegameSlotWriteDecision.Refused,
            SavegameSlotStates.DecideWrite(Slot("savegame3", occupied: true), Binding("savegame3", "aaaa"), "bbbb"));

        Assert.Equal(
            SavegameSlotWriteDecision.Allowed,
            SavegameSlotStates.DecideWrite(Slot("savegame4", occupied: false), null, null));
    }


    private static SavegameSlot Slot(string id, bool occupied) => new(
        new SavegameSlotId(id),
        occupied ? "Blackthorn Valley" : null,
        occupied,
        occupied ? DateTime.UtcNow : null,
        occupied ? TimeSpan.FromHours(12) : null);

    private static SavegameCheckoutBinding Binding(string slotId, string hash) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        slotId,
        Version: 4,
        ContentHash: hash,
        WrittenAt: DateTime.UtcNow);
}
