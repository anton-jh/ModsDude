using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Globalization;

namespace ModsDude.Client.Core.Retention;

/// <summary>What the server's retention will delete, as the rows that carry a date say it.</summary>
public enum RetainedKind
{
    Snapshot,
    Revision,
    ModVersion
}


/// <summary>
/// The words for a deletion date the server has scheduled. One place, so the three pages that show
/// one - saves, profile history and the repo's mods - say it the same way.
/// </summary>
/// <remarks>
/// The date is all a row shows; why is the tooltip. The reason matters because it says what would
/// keep the row: something outside the window is kept by being needed, something in a history that
/// is winding down is kept by the history being used again.
/// </remarks>
public static class ScheduledDeletion
{
    /// <returns>
    /// "To be deleted 7 October", with the year only when it is not this one. <c>null</c> where nothing
    /// is scheduled.
    /// </returns>
    public static string? Describe(DateOnly? date, DateOnly today)
    {
        if (date is not DateOnly scheduled)
        {
            return null;
        }

        var format = scheduled.Year == today.Year ? "d MMMM" : "d MMMM yyyy";

        return $"To be deleted {scheduled.ToString(format, CultureInfo.CurrentCulture)}";
    }

    /// <returns>Why the row goes, and what would keep it. <c>null</c> where nothing is scheduled.</returns>
    public static string? Explain(DeletionReason? reason, RetainedKind kind) => (reason, kind) switch
    {
        (DeletionReason.OutsideWindow, RetainedKind.Snapshot) =>
            "Not one of this savegame's 3 newest snapshots.",
        (DeletionReason.WindingDown, RetainedKind.Snapshot) =>
            "Nothing new has been checked in. Check the savegame in to keep it.",

        (DeletionReason.OutsideWindow, RetainedKind.Revision) =>
            "Not one of the profile's 3 newest revisions, and no savegame was played on it. Play a savegame on it to keep it.",
        (DeletionReason.WindingDown, RetainedKind.Revision) =>
            "No savegame has been played on this profile. Play one on it, or save a new revision, to keep it.",

        (DeletionReason.OutsideWindow, RetainedKind.ModVersion) =>
            "Not one of this mod's 2 newest versions, and no profile revision uses it. Add it to a profile to keep it.",
        (DeletionReason.WindingDown, RetainedKind.ModVersion) =>
            "No profile uses this mod. Add a version to a profile to keep it.",

        _ => null
    };
}
