using ModsDude.Client.Core.Profiles;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModListDiffTests
{
    [Fact]
    public void An_unchanged_list_writes_nothing()
    {
        var changes = ProfileModListDiff.Compute(
            [Pin("a", "1.0"), Pin("b", "2.0", byProfile: true)],
            [Pin("a", "1.0"), Pin("b", "2.0", byProfile: true)]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void A_mod_that_was_not_pinned_before_is_an_addition()
    {
        var changes = ProfileModListDiff.Compute([Pin("a", "1.0")], [Pin("a", "1.0"), Pin("b", "2.0")]);

        Assert.Equal(Mod("b"), Assert.Single(changes.Added).ModId);
        Assert.Empty(changes.Changed);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public void A_mod_that_is_no_longer_pinned_is_a_removal()
    {
        var changes = ProfileModListDiff.Compute([Pin("a", "1.0"), Pin("b", "2.0")], [Pin("a", "1.0")]);

        Assert.Equal(Mod("b"), Assert.Single(changes.Removed));
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Changed);
    }

    [Fact]
    public void A_different_version_of_the_same_mod_is_a_change_rather_than_a_swap()
    {
        var changes = ProfileModListDiff.Compute([Pin("a", "1.0")], [Pin("a", "1.1")]);

        Assert.Equal(V("1.1"), Assert.Single(changes.Changed).VersionId);
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public void Locking_a_mod_in_the_profile_is_a_change_on_its_own()
    {
        var changes = ProfileModListDiff.Compute([Pin("a", "1.0")], [Pin("a", "1.0", byProfile: true)]);

        Assert.True(Assert.Single(changes.Changed).Lock.ByProfile);
    }

    /// <summary>
    /// The adapter's flag lives on the mod version and no dependency request carries it, so a change
    /// in it must not produce a write that says nothing.
    /// </summary>
    [Fact]
    public void The_adapters_flag_changing_is_not_a_dependency_change()
    {
        var changes = ProfileModListDiff.Compute([Pin("a", "1.0")], [Pin("a", "1.0", byAdapter: true)]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Additions_changes_and_removals_are_reported_together()
    {
        var changes = ProfileModListDiff.Compute(
            [Pin("a", "1.0"), Pin("b", "1.0"), Pin("c", "1.0")],
            [Pin("a", "1.0"), Pin("b", "1.1"), Pin("d", "1.0")]);

        Assert.Equal(Mod("d"), Assert.Single(changes.Added).ModId);
        Assert.Equal(Mod("b"), Assert.Single(changes.Changed).ModId);
        Assert.Equal(Mod("c"), Assert.Single(changes.Removed));
        Assert.Equal(3, changes.Count);
    }


    private static ProfileModPin Pin(string modId, string version, bool byAdapter = false, bool byProfile = false)
        => new(Mod(modId), V(version), new ProfileModLock(byAdapter, byProfile));
}
