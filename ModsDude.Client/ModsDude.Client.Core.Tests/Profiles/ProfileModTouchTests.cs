using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles;

/// <summary>
/// What a draft has done to a mod is measured against the server's list, so it has to agree with
/// the two other places that ask the same question: what a save writes, and what history reports.
/// </summary>
public class ProfileModTouchTests
{
    [Fact]
    public void A_mod_only_the_draft_pins_is_added()
        => Assert.Equal(ProfileModTouch.Added, ProfileModTouches.Classify(null, Pin("a", "1.0")));

    [Fact]
    public void A_mod_only_the_server_pins_is_taken_out()
        => Assert.Equal(ProfileModTouch.TakenOut, ProfileModTouches.Classify(Pin("a", "1.0"), null));

    [Fact]
    public void A_mod_the_draft_has_not_touched_is_none()
        => Assert.Equal(ProfileModTouch.None, ProfileModTouches.Classify(Pin("a", "1.0"), Pin("a", "1.0")));

    [Fact]
    public void A_mod_on_neither_side_is_none()
        => Assert.Equal(ProfileModTouch.None, ProfileModTouches.Classify(null, null));

    [Fact]
    public void Moving_the_version_is_a_version_change()
        => Assert.Equal(ProfileModTouch.VersionChanged, ProfileModTouches.Classify(Pin("a", "1.0"), Pin("a", "1.1")));

    [Fact]
    public void Toggling_the_profiles_lock_is_a_lock_change()
        => Assert.Equal(
            ProfileModTouch.LockChanged,
            ProfileModTouches.Classify(Pin("a", "1.0"), Pin("a", "1.0", byProfile: true)));

    [Fact]
    public void Moving_the_version_and_the_lock_is_both()
        => Assert.Equal(
            ProfileModTouch.VersionAndLockChanged,
            ProfileModTouches.Classify(Pin("a", "1.0", byProfile: true), Pin("a", "1.1")));

    /// <summary>
    /// The adapter's flag belongs to the mod version, so it is nothing a draft can have done - the same
    /// rule the save's diff applies.
    /// </summary>
    [Fact]
    public void The_adapters_lock_is_not_a_touch()
        => Assert.Equal(
            ProfileModTouch.None,
            ProfileModTouches.Classify(Pin("a", "1.0"), Pin("a", "1.0", byAdapter: true)));

    [Fact]
    public void A_mod_nothing_has_been_done_to_has_nothing_to_say()
        => Assert.Null(ProfileModTouches.Describe(Pin("a", "1.0"), Pin("a", "1.0")));

    [Fact]
    public void A_version_change_says_what_it_was_and_what_it_is()
    {
        var text = ProfileModTouches.Describe(Pin("a", "1.0"), Pin("a", "1.1"));

        Assert.Contains("1.0", text);
        Assert.Contains("1.1", text);
    }

    [Fact]
    public void A_lock_change_says_which_way_it_went()
    {
        Assert.StartsWith("Locked", ProfileModTouches.Describe(Pin("a", "1.0"), Pin("a", "1.0", byProfile: true)));
        Assert.StartsWith("Unlocked", ProfileModTouches.Describe(Pin("a", "1.0", byProfile: true), Pin("a", "1.0")));
    }

    /// <summary>
    /// The row a comparison reports and the mark the editor draws are the same fact, so they come
    /// out of both routes identically - and a comparison is empty exactly when the save would write
    /// nothing.
    /// </summary>
    [Theory]
    [InlineData("a", "1.0", false, "a", "1.0", false)]
    [InlineData("a", "1.0", false, "a", "1.1", false)]
    [InlineData("a", "1.0", false, "a", "1.0", true)]
    [InlineData("a", "1.0", true, "a", "1.1", false)]
    [InlineData("a", "1.0", false, null, null, false)]
    [InlineData(null, null, false, "a", "1.0", false)]
    public void The_touch_a_comparison_reports_is_the_one_the_pins_classify_as(
        string? beforeMod, string? beforeVersion, bool beforeLocked,
        string? afterMod, string? afterVersion, bool afterLocked)
    {
        var before = beforeMod is null ? null : Pin(beforeMod, beforeVersion!, byProfile: beforeLocked);
        var after = afterMod is null ? null : Pin(afterMod, afterVersion!, byProfile: afterLocked);

        var comparison = ProfileRevisionComparison.Between(1, 2, Pinned(before), Pinned(after));
        var diff = ProfileModListDiff.Compute(Listed(before), Listed(after));

        var expected = ProfileModTouches.Classify(before, after);

        Assert.Equal(expected is ProfileModTouch.None, comparison.IsEmpty);
        Assert.Equal(comparison.IsEmpty, diff.IsEmpty);

        if (comparison.Changes.Count == 1)
        {
            Assert.Equal(expected, ProfileModTouches.Of(comparison.Changes[0]));
        }
    }


    private static ProfileModPin Pin(string modId, string version, bool byAdapter = false, bool byProfile = false)
        => new(Mod(modId), V(version), new ProfileModLock(byAdapter, byProfile));

    private static IReadOnlyList<ProfileModPin> Listed(ProfileModPin? pin) => pin is null ? [] : [pin];

    private static IReadOnlyList<PinnedMod> Pinned(ProfileModPin? pin)
        => pin is null
            ? []
            :
            [
                new PinnedMod(
                    new CatalogModVersion(
                        pin.ModId,
                        pin.VersionId,
                        pin.ModId.Value,
                        "",
                        IsLocal: false,
                        IsOnServer: true,
                        Locked: pin.Lock.ByAdapter),
                    pin.Lock)
            ];
}
