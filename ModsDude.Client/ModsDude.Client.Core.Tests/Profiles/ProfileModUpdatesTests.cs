using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModUpdatesTests
{
    [Fact]
    public void The_repos_newest_version_is_offered_as_the_update()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.0")],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1), Registered("map", "1.2", 2)]);

        var update = Assert.Single(plan.Available);

        Assert.Equal(V("1.0"), update.From);
        Assert.Equal(V("1.2"), update.To);
        Assert.Empty(plan.Skipped);
    }

    /// <summary>
    /// The sequence number is the order, not the version string - a repo whose versions were
    /// arbitrated into an order the strings do not imply still gets that order.
    /// </summary>
    [Fact]
    public void Newest_is_the_highest_sequence_number_rather_than_the_highest_version_string()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.9")],
            [Registered("map", "1.10", 0), Registered("map", "1.9", 1)]);

        Assert.Empty(plan.Available);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void A_pin_already_on_the_newest_version_is_not_an_update()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.2")],
            [Registered("map", "1.0", 0), Registered("map", "1.2", 1)]);

        Assert.False(plan.HasAny);
    }

    [Fact]
    public void A_newer_version_that_is_only_on_disk_is_not_an_update()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.0")],
            [Registered("map", "1.0", 0), LocalOnly("map", "1.1")]);

        Assert.False(plan.HasAny);
    }

    /// <summary>
    /// Nothing has placed the pinned version yet, so there is nothing for another version to be
    /// newer than. Offering one would be a guess about an order the repo has not settled.
    /// </summary>
    [Fact]
    public void A_pin_the_repo_does_not_hold_is_left_alone()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "2.0")],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.False(plan.HasAny);
    }

    [Fact]
    public void A_mod_the_catalog_knows_nothing_about_is_left_alone()
    {
        var plan = ProfileModUpdates.Plan([Pin("map", "1.0")], []);

        Assert.False(plan.HasAny);
    }

    [Fact]
    public void An_adapter_locked_mod_is_skipped_rather_than_updated()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.0", byAdapter: true)],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.Empty(plan.Available);

        var skipped = Assert.Single(plan.Skipped);

        Assert.Equal(V("1.1"), skipped.To);
        Assert.Equal(ProfileModLockSource.Adapter, skipped.Lock.Source);
    }

    [Fact]
    public void A_profile_locked_mod_is_skipped_rather_than_updated()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.0", byProfile: true)],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.Empty(plan.Available);
        Assert.Equal(ProfileModLockSource.Profile, Assert.Single(plan.Skipped).Lock.Source);
    }

    [Fact]
    public void A_mod_locked_at_both_levels_is_skipped_once()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("map", "1.0", byAdapter: true, byProfile: true)],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.Equal(ProfileModLockSource.Both, Assert.Single(plan.Skipped).Lock.Source);
    }

    [Fact]
    public void The_locked_and_the_unlocked_are_partitioned_and_both_counted()
    {
        var plan = ProfileModUpdates.Plan(
            [Pin("a", "1.0"), Pin("b", "1.0", byAdapter: true), Pin("c", "1.0"), Pin("d", "1.0")],
            [
                Registered("a", "1.0", 0), Registered("a", "1.1", 1),
                Registered("b", "1.0", 0), Registered("b", "1.1", 1),
                Registered("c", "1.0", 0), Registered("c", "1.1", 1),
                // Nothing newer, so it is not a candidate either way.
                Registered("d", "1.0", 0)
            ]);

        Assert.Equal([Mod("a"), Mod("c")], plan.Available.Select(x => x.ModId));
        Assert.Equal([Mod("b")], plan.Skipped.Select(x => x.ModId));
        Assert.Equal(3, plan.Count);
    }


    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void A_mod_is_locked_when_either_flag_is_set(bool byAdapter, bool byProfile, bool expected)
    {
        Assert.Equal(expected, new ProfileModLock(byAdapter, byProfile).IsLocked);
    }

    /// <summary>
    /// There is no repo-wide user override, so clearing the per-profile flag does not free a pin the
    /// adapter is holding. The wording on the row depends on knowing that.
    /// </summary>
    [Fact]
    public void Unlocking_in_the_profile_does_not_free_a_pin_the_adapter_holds()
    {
        Assert.False(new ProfileModLock(ByAdapter: true, ByProfile: true).CanBeUnlockedByProfile);
        Assert.True(new ProfileModLock(ByAdapter: false, ByProfile: true).CanBeUnlockedByProfile);
    }


    private static ProfileModPin Pin(string modId, string version, bool byAdapter = false, bool byProfile = false)
        => new(Mod(modId), V(version), new ProfileModLock(byAdapter, byProfile));

    private static CatalogModVersion Registered(string modId, string version, int sequenceNumber)
        => new(Mod(modId), V(version), modId, "", IsLocal: false, IsOnServer: true, Locked: false)
        {
            SequenceNumber = sequenceNumber
        };

    private static CatalogModVersion LocalOnly(string modId, string version)
        => new(Mod(modId), V(version), modId, "", IsLocal: true, IsOnServer: false, Locked: false);
}
