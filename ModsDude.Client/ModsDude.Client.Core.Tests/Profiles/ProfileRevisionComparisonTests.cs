using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

/// <summary>
/// What "which mods changed" means between two revisions. The revision's own stored counts say how
/// many; this is the same question answered mod by mod, and the two have to agree.
/// </summary>
public class ProfileRevisionComparisonTests
{
    [Fact]
    public void A_mod_only_the_newer_revision_pins_is_an_addition()
    {
        var comparison = Compare([], [Pin("A", "1.0.0")]);

        var change = Assert.Single(comparison.Changes);

        Assert.Equal(ProfileModChangeKind.Added, change.Kind);
        Assert.Null(change.FromVersionId);
        Assert.Equal("1.0.0", change.ToVersionId?.Value);
    }

    [Fact]
    public void A_mod_only_the_older_revision_pins_is_a_removal()
    {
        var comparison = Compare([Pin("A", "1.0.0")], []);

        var change = Assert.Single(comparison.Changes);

        Assert.Equal(ProfileModChangeKind.Removed, change.Kind);
        Assert.Equal("1.0.0", change.FromVersionId?.Value);
        Assert.Null(change.ToVersionId);
    }

    /// <summary>
    /// One row saying what it moved from, rather than a removal and an addition the reader has to
    /// pair up themselves - which is possible only because a profile pins each mod exactly once.
    /// </summary>
    [Fact]
    public void A_mod_that_moved_version_is_one_row_carrying_both_versions()
    {
        var comparison = Compare([Pin("A", "1.0.0")], [Pin("A", "2.0.0")]);

        var change = Assert.Single(comparison.Changes);

        Assert.Equal(ProfileModChangeKind.Changed, change.Kind);
        Assert.True(change.VersionMoved);
        Assert.Equal("1.0.0", change.FromVersionId?.Value);
        Assert.Equal("2.0.0", change.ToVersionId?.Value);
    }

    [Fact]
    public void A_mod_whose_lock_was_toggled_is_a_change_with_no_version_move()
    {
        var comparison = Compare([Pin("A", "1.0.0")], [Pin("A", "1.0.0", locked: true)]);

        var change = Assert.Single(comparison.Changes);

        Assert.Equal(ProfileModChangeKind.Changed, change.Kind);
        Assert.False(change.VersionMoved);
        Assert.True(change.LockChanged);
        Assert.True(change.ToLocked);
    }

    /// <summary>
    /// The adapter's lock belongs to the mod version rather than to the revision, so a mod
    /// re-registered as version-sensitive would otherwise read as an edit somebody made.
    /// </summary>
    [Fact]
    public void The_adapters_lock_is_not_a_change_to_the_profile()
    {
        var comparison = Compare(
            [Pin("A", "1.0.0", lockedByAdapter: false)],
            [Pin("A", "1.0.0", lockedByAdapter: true)]);

        Assert.True(comparison.IsEmpty);
    }

    /// <summary>
    /// Comparing a restore with what it restored is the ordinary way two different revisions hold
    /// the same list, and it has to come back empty rather than as a failure.
    /// </summary>
    [Fact]
    public void Two_revisions_pinning_the_same_list_compare_as_no_change()
    {
        var pins = new[] { Pin("A", "1.0.0"), Pin("B", "2.0.0", locked: true) };

        Assert.True(Compare(pins, [.. pins.Reverse()]).IsEmpty);
    }

    [Fact]
    public void The_counts_match_what_the_rows_say()
    {
        var comparison = Compare(
            [Pin("A", "1.0.0"), Pin("B", "1.0.0"), Pin("C", "1.0.0")],
            [Pin("A", "1.0.0"), Pin("B", "2.0.0"), Pin("D", "1.0.0")]);

        Assert.Equal(1, comparison.AddedCount);
        Assert.Equal(1, comparison.ChangedCount);
        Assert.Equal(1, comparison.RemovedCount);
        Assert.Equal(3, comparison.Changes.Count);
    }

    [Fact]
    public void Rows_are_ordered_new_then_moved_then_gone()
    {
        var comparison = Compare(
            [Pin("B", "1.0.0"), Pin("C", "1.0.0")],
            [Pin("A", "1.0.0"), Pin("B", "2.0.0")]);

        Assert.Equal(
            [ProfileModChangeKind.Added, ProfileModChangeKind.Changed, ProfileModChangeKind.Removed],
            comparison.Changes.Select(x => x.Kind));
    }

    /// <summary>
    /// A removed mod is only described by the side it still exists on, so its row has to come from
    /// the older revision - the newer one has nothing to say about it.
    /// </summary>
    [Fact]
    public void A_removal_is_rendered_from_the_revision_that_still_had_it()
    {
        var comparison = Compare([Pin("A", "1.0.0")], []);

        Assert.Equal("1.0.0", Assert.Single(comparison.Changes).Version.VersionId.Value);
    }


    private static ProfileRevisionComparison Compare(IReadOnlyList<PinnedMod> before, IReadOnlyList<PinnedMod> after)
        => ProfileRevisionComparison.Between(1, 2, before, after);

    private static PinnedMod Pin(string modId, string versionId, bool locked = false, bool lockedByAdapter = false)
        => new(
            new CatalogModVersion(
                ModKey.From(modId),
                ModVersionKey.From(versionId),
                modId,
                "",
                IsLocal: false,
                IsOnServer: true,
                Locked: lockedByAdapter),
            new ProfileModLock(lockedByAdapter, locked));
}
