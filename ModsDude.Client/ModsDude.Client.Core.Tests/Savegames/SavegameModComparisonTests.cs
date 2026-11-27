using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>How the mods a savegame lists differ from what a profile revision pins.</summary>
public class SavegameModComparisonTests
{
    [Fact]
    public void A_listed_mod_the_profile_does_not_pin_is_not_in_the_profile()
    {
        var comparison = Compare([Listed("a", "1.0"), Listed("both", "1.0")], [Pin("both", "1.0")]);

        Assert.Equal([new SavegameModDifference(Mod("a"), V("1.0"), null, false)], comparison.NotInProfile);
        Assert.Empty(comparison.NotInSavegame);
        Assert.Empty(comparison.OtherVersion);
    }

    [Fact]
    public void A_pinned_mod_the_save_does_not_list_is_not_in_the_savegame()
    {
        var comparison = Compare([Listed("both", "1.0")], [Pin("both", "1.0"), Pin("b", "2.0")]);

        Assert.Equal([new SavegameModDifference(Mod("b"), null, V("2.0"), false)], comparison.NotInSavegame);
        Assert.Empty(comparison.NotInProfile);
    }

    [Fact]
    public void A_mod_pinned_at_another_version_than_the_save_recorded_is_another_version()
    {
        var comparison = Compare([Listed("m", "1.0")], [Pin("m", "2.0")]);

        Assert.Equal([new SavegameModDifference(Mod("m"), V("1.0"), V("2.0"), false)], comparison.OtherVersion);
        Assert.Empty(comparison.NotInProfile);
        Assert.Empty(comparison.NotInSavegame);
    }

    [Fact]
    public void A_mod_the_save_recorded_no_version_of_is_never_another_version()
    {
        var comparison = Compare([Listed("m", null)], [Pin("m", "2.0")]);

        Assert.True(comparison.IsEmpty);
    }

    [Fact]
    public void The_same_mods_at_the_same_versions_are_no_difference()
    {
        var comparison = Compare([Listed("a", "1.0"), Listed("b", "2.0")], [Pin("b", "2.0"), Pin("a", "1.0")]);

        Assert.True(comparison.IsEmpty);
        Assert.Empty(comparison.Locked);
    }

    [Fact]
    public void A_mod_the_game_or_the_profile_locks_is_a_locked_difference()
    {
        var comparison = Compare(
            [Listed("map", "1.0"), Listed("profilelocked", "1.0"), Listed("free", "1.0")],
            [Pin("map", "2.0"), Pin("profilelocked", "2.0", locked: true), Pin("otherfree", "1.0")],
            lockedMods: ["map"]);

        Assert.Equal(["map", "profilelocked"], comparison.Locked.Select(x => x.ModId.Value));
        Assert.All(comparison.OtherVersion, x => Assert.True(x.IsLocked));
        Assert.False(Assert.Single(comparison.NotInProfile).IsLocked);
    }

    [Fact]
    public void A_locked_map_the_profile_does_not_pin_is_a_locked_difference()
    {
        var comparison = Compare([Listed("map", "1.0")], [], lockedMods: ["map"]);

        Assert.True(Assert.Single(comparison.NotInProfile).IsLocked);
    }

    [Fact]
    public void The_result_is_the_same_whatever_order_the_inputs_arrive_in()
    {
        SavegameModReference[] listed = [Listed("c", "1.0"), Listed("a", "1.0"), Listed("b", "1.0"), Listed("e", "1.0")];
        ProfileModPin[] pins = [Pin("e", "2.0"), Pin("d", "1.0"), Pin("f", "1.0"), Pin("b", "1.0")];

        var forwards = Compare(listed, pins);
        var backwards = Compare([.. listed.Reverse()], [.. pins.Reverse()]);

        Assert.Equal(["a", "c"], forwards.NotInProfile.Select(x => x.ModId.Value));
        Assert.Equal(["d", "f"], forwards.NotInSavegame.Select(x => x.ModId.Value));
        Assert.Equal(forwards.NotInProfile, backwards.NotInProfile);
        Assert.Equal(forwards.NotInSavegame, backwards.NotInSavegame);
        Assert.Equal(forwards.OtherVersion, backwards.OtherVersion);
    }


    private static SavegameModComparison Compare(
        IReadOnlyList<SavegameModReference> listed,
        IReadOnlyList<ProfileModPin> pins,
        params string[] lockedMods)
        => SavegameModComparison.Compare(listed, pins, lockedMods.Select(Mod).ToHashSet());

    private static SavegameModReference Listed(string modId, string? version)
        => new(Mod(modId), version is null ? null : V(version));

    private static ProfileModPin Pin(string modId, string version, bool locked = false)
        => new(Mod(modId), V(version), new ProfileModLock(false, locked));
}
