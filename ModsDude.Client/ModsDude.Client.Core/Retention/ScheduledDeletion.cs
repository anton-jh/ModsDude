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

    public static string? Describe(DateOnly? date)
        => Describe(date, DateOnly.FromDateTime(DateTime.Now));

    /// <returns>Why the row goes, and what would keep it. <c>null</c> where nothing is scheduled.</returns>
    public static string? Explain(DeletionReason? reason, RetainedKind kind) => (reason, kind) switch
    {
        (DeletionReason.OutsideWindow, RetainedKind.Snapshot) =>
            "Older than this save's three newest snapshots, so it is deleted on this date.",
        (DeletionReason.WindingDown, RetainedKind.Snapshot) =>
            "Nothing newer has been checked in, so this save's history is winding down to its current snapshot. "
                + "Checking the save in again keeps it.",

        (DeletionReason.OutsideWindow, RetainedKind.Revision) =>
            "Older than the profile's three newest revisions and no savegame was played on it, so it is deleted on this date. "
                + "Playing a savegame on it keeps it.",
        (DeletionReason.WindingDown, RetainedKind.Revision) =>
            "No savegame has been played on this profile, so its history is winding down to the current revision. "
                + "Playing a savegame on it, or saving a new revision, keeps it.",

        (DeletionReason.OutsideWindow, RetainedKind.ModVersion) =>
            "Older than this mod's two newest versions and no profile revision uses it, so it is deleted on this date. "
                + "Adding it to a profile keeps it.",
        (DeletionReason.WindingDown, RetainedKind.ModVersion) =>
            "No profile uses any version of this mod, so it is winding down to its newest version. "
                + "Adding either version to a profile keeps it.",

        _ => null
    };
}
