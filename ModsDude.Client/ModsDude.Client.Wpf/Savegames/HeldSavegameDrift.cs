using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>What the last drift check said about one held savegame.</summary>
internal static class HeldSavegameDrift
{
    /// <summary>Whether it has been played here since it was checked out.</summary>
    public static bool HasUncheckedInPlay(IEnumerable<TargetDrift> drifted, Guid savegameId)
        => Of(drifted, savegameId).Any(x => x.Kind is SavegameDriftKind.UncheckedInPlay);

    /// <summary>What the game calls the save in its slot, where the check could read it.</summary>
    public static string? SlotName(IEnumerable<TargetDrift> drifted, Guid savegameId)
        => Of(drifted, savegameId).Select(x => x.SlotDisplayName).FirstOrDefault(x => x is { Length: > 0 });


    private static IEnumerable<SavegameDrift> Of(IEnumerable<TargetDrift> drifted, Guid savegameId)
        => drifted.SelectMany(x => x.Report.SavegameDrift).Where(x => x.SavegameId == savegameId);
}
