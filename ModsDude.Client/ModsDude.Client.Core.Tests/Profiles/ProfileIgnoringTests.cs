using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileIgnoringTests
{
    private static readonly IReadOnlySet<ModKey> _none = new HashSet<ModKey>();


    [Fact]
    public void A_mod_nobody_has_ignored_or_pinned_is_an_ordinary_row()
    {
        Assert.Equal(IgnoreState.None, Classify("Map", "1.0", ignored: [], pinned: []));
    }

    [Fact]
    public void An_ignored_mod_is_ignored()
    {
        Assert.Equal(IgnoreState.Ignored, Classify("Map", "1.0", ignored: ["Map"], pinned: []));
    }

    /// <summary>
    /// A pinned mod cannot also be ignored. The draft may pin a mod the server still lists as ignored -
    /// the release happens at save - and until then the pin is what the row is read against.
    /// </summary>
    [Fact]
    public void A_pinned_mod_is_never_ignored_even_where_the_server_still_lists_it()
    {
        var state = Classify("Map", "1.1", ignored: ["Map"], pinned: [("Map", "1.0", false)]);

        Assert.Equal(IgnoreState.Pinned, state);
    }

    [Fact]
    public void Another_version_of_a_locked_pin_is_ignored_without_anybody_saying_so()
    {
        var state = Classify("Map", "1.1", ignored: [], pinned: [("Map", "1.0", true)]);

        Assert.Equal(IgnoreState.OtherVersionOfLocked, state);
    }

    /// <summary>
    /// The lock is about the pin, not about the mod: the version the profile holds is not "another"
    /// one, and would be hidden by being pinned long before this was asked.
    /// </summary>
    [Fact]
    public void The_locked_version_itself_is_not_another_version()
    {
        var state = Classify("Map", "1.0", ignored: [], pinned: [("Map", "1.0", true)]);

        Assert.Equal(IgnoreState.Pinned, state);
    }

    [Fact]
    public void Another_version_of_an_unlocked_pin_is_an_update_and_not_ignored()
    {
        var state = Classify("Map", "1.1", ignored: [], pinned: [("Map", "1.0", false)]);

        Assert.Equal(IgnoreState.Pinned, state);
    }

    [Fact]
    public void A_lock_on_one_mod_says_nothing_about_another()
    {
        var state = Classify("Tractor", "1.1", ignored: [], pinned: [("Map", "1.0", true)]);

        Assert.Equal(IgnoreState.None, state);
    }


    [Fact]
    public void A_pinned_mod_is_left_out_of_the_list_that_is_written()
    {
        var written = ProfileIgnoring.WithoutPinned([Mod("Map"), Mod("Tractor")], new HashSet<ModKey> { Mod("Map") });

        Assert.Equal([Mod("Tractor")], written);
    }

    [Fact]
    public void Nothing_pinned_leaves_the_list_as_it_was()
    {
        var written = ProfileIgnoring.WithoutPinned([Mod("Map"), Mod("Tractor")], _none);

        Assert.Equal(2, written.Count);
    }

    private static IgnoreState Classify(
        string modId,
        string version,
        string[] ignored,
        (string Mod, string Version, bool Locked)[] pinned)
    {
        return ProfileIgnoring.Classify(
            Mod(modId),
            V(version),
            ignored.Length == 0 ? _none : ignored.Select(Mod).ToHashSet(),
            pinned.ToDictionary(x => Mod(x.Mod), x => (V(x.Version), x.Locked)));
    }
}
