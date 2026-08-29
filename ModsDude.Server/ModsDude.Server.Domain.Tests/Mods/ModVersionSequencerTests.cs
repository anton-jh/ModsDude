using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Tests.Mods;

public class ModVersionSequencerTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly ModId _modId = new("FS25_TestMod");
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public void Appending_to_an_empty_set_takes_the_first_sequence_number()
    {
        var siblings = Siblings();

        Assert.Equal(0, ModVersionSequencer.MakeRoomAt(siblings, after: null, before: null, _timestamp));
    }

    [Fact]
    public void Appending_after_the_last_version_takes_the_sequence_number_after_it()
    {
        var siblings = Siblings("a", "b", "c");

        var sequenceNumber = ModVersionSequencer.MakeRoomAt(siblings, after: Version("c"), before: null, _timestamp);

        Assert.Equal(3, sequenceNumber);
        AssertOrder(siblings, "a", "b", "c");
    }

    [Fact]
    public void Inserting_between_two_neighbours_takes_the_later_one_s_place_and_shifts_it_up()
    {
        var siblings = Siblings("a", "b", "c");

        var sequenceNumber = ModVersionSequencer.MakeRoomAt(siblings, after: Version("a"), before: Version("b"), _timestamp);

        Assert.Equal(1, sequenceNumber);
        AssertOrder(siblings, "a", null, "b", "c");
    }

    [Fact]
    public void Inserting_before_the_first_version_shifts_every_sibling_up_by_one()
    {
        var siblings = Siblings("a", "b", "c");

        var sequenceNumber = ModVersionSequencer.MakeRoomAt(siblings, after: null, before: Version("a"), _timestamp);

        Assert.Equal(0, sequenceNumber);
        AssertOrder(siblings, null, "a", "b", "c");
    }

    [Fact]
    public void Shifting_a_sibling_up_stamps_it_as_updated()
    {
        var siblings = Siblings("a", "b");
        var later = siblings.Single(x => x.Id == Version("b"));

        ModVersionSequencer.MakeRoomAt(siblings, after: Version("a"), before: Version("b"), _timestamp);

        Assert.Equal(_timestamp, later.Updated);
    }

    [Fact]
    public void Closing_the_gap_after_a_removal_pulls_every_later_version_down_by_one()
    {
        var siblings = Siblings("a", "b", "c", "d");
        var removed = siblings.Single(x => x.Id == Version("b"));

        ModVersionSequencer.CloseGap([.. siblings.Where(x => x != removed)], removed, _timestamp);

        AssertOrder([.. siblings.Where(x => x != removed)], "a", "c", "d");
    }

    [Fact]
    public void Closing_the_gap_after_removing_the_last_version_leaves_the_others_alone()
    {
        var siblings = Siblings("a", "b", "c");
        var removed = siblings.Single(x => x.Id == Version("c"));

        ModVersionSequencer.CloseGap([.. siblings.Where(x => x != removed)], removed, _timestamp);

        AssertOrder([.. siblings.Where(x => x != removed)], "a", "b");
    }

    [Fact]
    public void A_run_of_inserts_and_removals_leaves_the_ordering_contiguous()
    {
        var siblings = Siblings("a", "b", "c");

        Insert(siblings, "x", after: null, before: Version("a"));
        Insert(siblings, "y", after: Version("b"), before: Version("c"));
        Insert(siblings, "z", after: Version("c"), before: null);

        var removed = siblings.Single(x => x.Id == Version("b"));
        siblings.Remove(removed);
        ModVersionSequencer.CloseGap(siblings, removed, _timestamp);

        AssertOrder(siblings, "x", "a", "y", "c", "z");
    }


    [Fact]
    public void A_placement_naming_no_neighbours_is_rejected_against_a_non_empty_set()
    {
        var siblings = Siblings("a", "b");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: null, before: null));
    }

    [Fact]
    public void A_placement_naming_no_neighbours_is_accepted_against_an_empty_set()
    {
        Assert.True(ModVersionSequencer.CheckPlacementIsValid(Siblings(), after: null, before: null));
    }

    [Fact]
    public void A_placement_whose_after_is_no_longer_the_last_version_is_rejected()
    {
        // The client computed "append after b" against an order that has since grown a c.
        var siblings = Siblings("a", "b", "c");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: Version("b"), before: null));
    }

    [Fact]
    public void A_placement_whose_before_is_no_longer_the_first_version_is_rejected()
    {
        var siblings = Siblings("a", "b", "c");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: null, before: Version("b")));
    }

    [Fact]
    public void A_placement_whose_neighbours_are_no_longer_adjacent_is_rejected()
    {
        // Someone else inserted b between a and c since the placement was computed. Asserting both
        // neighbours is the whole point: against a alone this would still look placeable, and would
        // silently order the new version ahead of b.
        var siblings = Siblings("a", "b", "c");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: Version("a"), before: Version("c")));
    }

    [Fact]
    public void A_placement_whose_neighbours_are_adjacent_but_reversed_is_rejected()
    {
        var siblings = Siblings("a", "b");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: Version("b"), before: Version("a")));
    }

    [Fact]
    public void A_placement_naming_an_unknown_after_is_rejected()
    {
        var siblings = Siblings("a", "b");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: Version("gone"), before: null));
    }

    [Fact]
    public void A_placement_naming_an_unknown_before_is_rejected()
    {
        var siblings = Siblings("a", "b");

        Assert.False(ModVersionSequencer.CheckPlacementIsValid(siblings, after: null, before: Version("gone")));
    }

    [Fact]
    public void A_placement_between_two_adjacent_neighbours_is_accepted()
    {
        var siblings = Siblings("a", "b", "c");

        Assert.True(ModVersionSequencer.CheckPlacementIsValid(siblings, after: Version("b"), before: Version("c")));
    }


    /// <summary>
    /// Regression: <c>GetNextSequenceNumberForVersion</c> returned the highest sequence number in
    /// use rather than the one after it, so every appended version collided with the one before it.
    /// </summary>
    [Fact]
    public void Regression_appending_does_not_reuse_the_last_sequence_number()
    {
        var siblings = Siblings("a", "b", "c");

        var sequenceNumber = ModVersionSequencer.MakeRoomAt(siblings, after: Version("c"), before: null, _timestamp);

        Assert.DoesNotContain(sequenceNumber, siblings.Select(x => x.SequenceNumber));
    }

    /// <summary>
    /// Regression: removal shifted the later versions down before checking that the version being
    /// removed belonged to the set, so a rejected removal left the ordering renumbered anyway. The
    /// same rule holds on the insert side, which is where the validation now lives.
    /// </summary>
    [Fact]
    public void Regression_a_rejected_placement_leaves_the_ordering_untouched()
    {
        var siblings = Siblings("a", "b", "c");

        Assert.Throws<InvalidOperationException>(
            () => ModVersionSequencer.MakeRoomAt(siblings, after: Version("a"), before: Version("c"), _timestamp));

        AssertOrder(siblings, "a", "b", "c");
        Assert.All(siblings, x => Assert.Equal(default(DateTimeOffset), x.Updated));
    }


    private static ModVersionId Version(string id) => new(id);

    private static List<ModVersion> Siblings(params string[] versionIds) =>
        [.. versionIds.Select((versionId, index) => CreateVersion(versionId, index))];

    private static void Insert(List<ModVersion> siblings, string versionId, ModVersionId? after, ModVersionId? before)
    {
        var sequenceNumber = ModVersionSequencer.MakeRoomAt(siblings, after, before, _timestamp);

        siblings.Add(CreateVersion(versionId, sequenceNumber));
    }

    /// <summary>
    /// Asserts the ordering by position rather than by number, so that it also fails on a set that
    /// is ordered correctly but no longer contiguous. A null entry stands for the version the caller
    /// is about to add at that position and has not added yet.
    /// </summary>
    private static void AssertOrder(IReadOnlyCollection<ModVersion> siblings, params string?[] expectedVersionIds)
    {
        var expected = expectedVersionIds.Where(x => x is not null).Select(x => x!);
        var actual = siblings.OrderBy(x => x.SequenceNumber).ToList();

        Assert.Equal(expected, actual.Select(x => x.Id.Value));
        Assert.Equal(
            Enumerable.Range(0, expectedVersionIds.Length).Where(x => expectedVersionIds[x] is not null),
            actual.Select(x => x.SequenceNumber));
    }

    private static ModVersion CreateVersion(string versionId, int sequenceNumber) => new()
    {
        RepoId = _repoId,
        ModId = _modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = sequenceNumber,
        DisplayName = versionId,
        Description = "",
        ContentHash = versionId,
        Locked = false,
        Attributes = [],
        Created = default,
        Updated = default
    };
}
