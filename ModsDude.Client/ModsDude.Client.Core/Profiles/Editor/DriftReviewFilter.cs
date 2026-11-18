using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>Which pinned mods the editor shows when it is opened to review a drifted mod folder.</summary>
public static class DriftReviewFilter
{
    /// <summary>
    /// Updates where the game only replaced mods with other versions, since those pins are still
    /// offered by the folder and so never show as not in the sources.
    /// </summary>
    public static PinnedModFilter For(DriftReport report)
        => report.IsUpdatesOnly ? PinnedModFilter.Updates : PinnedModFilter.NotInSources;

    /// <summary>The same across several of a game's folders: updates only where every one of them is.</summary>
    public static PinnedModFilter For(IReadOnlyCollection<DriftReport> reports)
        => reports.Count > 0 && reports.All(x => x.IsUpdatesOnly) ? PinnedModFilter.Updates : PinnedModFilter.NotInSources;
}
