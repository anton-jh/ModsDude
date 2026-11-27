using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// How the mods a savegame lists differ from what a profile revision pins, by mod id. Each list is
/// sorted by mod id.
/// </summary>
/// <param name="NotInProfile">Listed by the save and not pinned.</param>
/// <param name="NotInSavegame">Pinned and not listed by the save.</param>
/// <param name="OtherVersion">Pinned at a version other than the one the save recorded. Never a mod the save recorded no version of.</param>
public sealed record SavegameModComparison(
    IReadOnlyList<SavegameModDifference> NotInProfile,
    IReadOnlyList<SavegameModDifference> NotInSavegame,
    IReadOnlyList<SavegameModDifference> OtherVersion)
{
    public bool IsEmpty => NotInProfile.Count == 0 && NotInSavegame.Count == 0 && OtherVersion.Count == 0;

    /// <summary>Every locked mod among the differences, sorted by mod id.</summary>
    public IReadOnlyList<SavegameModDifference> Locked
        => [.. NotInProfile.Concat(NotInSavegame).Concat(OtherVersion)
            .Where(x => x.IsLocked)
            .OrderBy(x => x.ModId.Value, StringComparer.Ordinal)];


    /// <param name="lockedMods">Mods the game locks, such as maps, whichever version.</param>
    public static SavegameModComparison Compare(
        IReadOnlyList<SavegameModReference> savegame,
        IReadOnlyList<ProfileModPin> pins,
        IReadOnlySet<ModKey> lockedMods)
    {
        var listed = savegame.GroupBy(x => x.ModId).ToDictionary(x => x.Key, x => x.First());
        var pinned = pins.GroupBy(x => x.ModId).ToDictionary(x => x.Key, x => x.First());

        bool IsLocked(ModKey modId)
            => lockedMods.Contains(modId) || (pinned.TryGetValue(modId, out var pin) && pin.Lock.IsLocked);

        var notInProfile = listed.Values
            .Where(x => pinned.ContainsKey(x.ModId) is false)
            .Select(x => new SavegameModDifference(x.ModId, x.Version, null, IsLocked(x.ModId)));

        var notInSavegame = pinned.Values
            .Where(x => listed.ContainsKey(x.ModId) is false)
            .Select(x => new SavegameModDifference(x.ModId, null, x.VersionId, IsLocked(x.ModId)));

        var otherVersion = listed.Values
            .Where(x => x.Version is ModVersionKey recorded
                && pinned.TryGetValue(x.ModId, out var pin)
                && pin.VersionId != recorded)
            .Select(x => new SavegameModDifference(x.ModId, x.Version, pinned[x.ModId].VersionId, IsLocked(x.ModId)));

        return new SavegameModComparison(Sorted(notInProfile), Sorted(notInSavegame), Sorted(otherVersion));
    }


    private static List<SavegameModDifference> Sorted(IEnumerable<SavegameModDifference> differences)
        => [.. differences.OrderBy(x => x.ModId.Value, StringComparer.Ordinal)];
}


/// <param name="InSavegame">The version the save recorded, or null where it recorded none or does not list the mod.</param>
/// <param name="InProfile">The version the profile pins, or null where it does not pin the mod.</param>
/// <param name="IsLocked">The game locks the mod, or the profile does.</param>
public sealed record SavegameModDifference(ModKey ModId, ModVersionKey? InSavegame, ModVersionKey? InProfile, bool IsLocked);
