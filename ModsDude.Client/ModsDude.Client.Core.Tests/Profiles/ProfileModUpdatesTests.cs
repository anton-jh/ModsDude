using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Profiles;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModUpdatesTests
{
    [Fact]
    public void The_repos_newest_version_is_offered_as_the_update()
    {
        var plan = Plan(
            [Pin("map", "1.0")],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1), Registered("map", "1.2", 2)]);

        var update = Assert.Single(plan.Available);

        Assert.Equal(V("1.0"), update.From);
        Assert.Equal(V("1.2"), update.To);
        Assert.False(update.ImportsOnSave);
        Assert.Empty(plan.Skipped);
    }

    /// <summary>
    /// The sequence number is the order, not the version string - a repo whose versions were
    /// arbitrated into an order the strings do not imply still gets that order, and the comparer is
    /// never asked about a pair the repo has already settled.
    /// </summary>
    [Fact]
    public void Newest_is_the_highest_sequence_number_rather_than_the_highest_version_string()
    {
        var plan = Plan(
            [Pin("map", "1.9")],
            [Registered("map", "1.10", 0), Registered("map", "1.9", 1)]);

        Assert.Empty(plan.Available);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void A_pin_already_on_the_newest_version_is_not_an_update()
    {
        var plan = Plan(
            [Pin("map", "1.2")],
            [Registered("map", "1.0", 0), Registered("map", "1.2", 1)]);

        Assert.False(plan.HasAny);
    }

    /// <summary>
    /// The mod somebody has just downloaded, which is the errand this page exists for. It counts,
    /// and it says that taking it means uploading the file first.
    /// </summary>
    [Fact]
    public void A_newer_version_that_is_only_on_disk_is_an_update_that_imports_on_save()
    {
        var plan = Plan(
            [Pin("map", "1.0")],
            [Registered("map", "1.0", 0), LocalOnly("map", "1.1")]);

        var update = Assert.Single(plan.Available);

        Assert.Equal(V("1.1"), update.To);
        Assert.True(update.ImportsOnSave);
        Assert.Equal(1, plan.PendingCount);
        Assert.Equal(0, plan.FreeCount);
    }

    /// <summary>
    /// Offering a possible downgrade as an update is worse than saying nothing, so a version the
    /// comparer will not place against the pin is not one.
    /// </summary>
    [Fact]
    public void An_unregistered_version_the_comparer_abstains_on_is_not_an_update()
    {
        var plan = Plan(
            [Pin("map", "1.0")],
            [Registered("map", "1.0", 0), LocalOnly("map", "2024.03")]);

        Assert.False(plan.HasAny);
    }

    /// <summary>
    /// An abstention says nothing about the versions behind it, so the walk steps over it rather
    /// than stopping - the repo's own newer version is still an update.
    /// </summary>
    [Fact]
    public void A_version_the_comparer_cannot_place_does_not_hide_a_settled_one_below_it()
    {
        var plan = Plan(
            [Pin("map", "1.0")],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1), LocalOnly("map", "2024.03")]);

        var update = Assert.Single(plan.Available);

        Assert.Equal(V("1.1"), update.To);
        Assert.False(update.ImportsOnSave);
    }

    /// <summary>
    /// The pin's own version is not registered either - which used to mean nothing could be newer
    /// than it. The comparer places both, so the newer one counts.
    /// </summary>
    [Fact]
    public void A_pin_the_repo_does_not_hold_can_still_have_a_newer_version()
    {
        var plan = Plan(
            [Pin("map", "2.0")],
            [Registered("map", "1.0", 0), LocalOnly("map", "2.0"), LocalOnly("map", "2.1")]);

        Assert.Equal(V("2.1"), Assert.Single(plan.Available).To);
    }

    /// <summary>
    /// Nothing here has placed the pinned version at all, so there is nothing for another version to
    /// be newer than.
    /// </summary>
    [Fact]
    public void A_pin_no_source_holds_is_left_alone()
    {
        var plan = Plan(
            [Pin("map", "2.0")],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.False(plan.HasAny);
    }

    [Fact]
    public void A_mod_the_catalog_knows_nothing_about_is_left_alone()
    {
        var plan = Plan([Pin("map", "1.0")], []);

        Assert.False(plan.HasAny);
    }

    [Fact]
    public void An_older_version_sitting_on_disk_is_not_an_update()
    {
        var plan = Plan(
            [Pin("map", "1.2")],
            [Registered("map", "1.2", 0), LocalOnly("map", "1.1")]);

        Assert.False(plan.HasAny);
    }

    [Fact]
    public void An_adapter_locked_mod_is_skipped_rather_than_updated()
    {
        var plan = Plan(
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
        var plan = Plan(
            [Pin("map", "1.0", byProfile: true)],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.Empty(plan.Available);
        Assert.Equal(ProfileModLockSource.Profile, Assert.Single(plan.Skipped).Lock.Source);
    }

    [Fact]
    public void A_mod_locked_at_both_levels_is_skipped_once()
    {
        var plan = Plan(
            [Pin("map", "1.0", byAdapter: true, byProfile: true)],
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1)]);

        Assert.Equal(ProfileModLockSource.Both, Assert.Single(plan.Skipped).Lock.Source);
    }

    [Fact]
    public void The_locked_and_the_unlocked_are_partitioned_and_both_counted()
    {
        var plan = Plan(
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

    /// <summary>
    /// The band says the split rather than one total, because the two cost differently: one is a pin
    /// moving and the other is a file going up first.
    /// </summary>
    [Fact]
    public void The_two_kinds_of_update_are_counted_apart()
    {
        var plan = Plan(
            [Pin("a", "1.0"), Pin("b", "1.0"), Pin("c", "1.0", byProfile: true)],
            [
                Registered("a", "1.0", 0), Registered("a", "1.1", 1),
                Registered("b", "1.0", 0), LocalOnly("b", "1.1"),
                Registered("c", "1.0", 0), LocalOnly("c", "1.1")
            ]);

        Assert.Equal(3, plan.Count);
        Assert.Equal(2, plan.PendingCount);
        Assert.Equal(1, plan.FreeCount);
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


    private static ProfileModUpdatePlan Plan(
        IReadOnlyList<ProfileModPin> pins,
        IReadOnlyList<CatalogModVersion> catalog)
        => ProfileModUpdates.Plan(pins, catalog, DefaultModVersionComparer.Instance);

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
