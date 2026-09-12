using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One game as an overview shows it: where it installs, which profile it is meant to match,
/// and whether its mod folder still does. Read-only and rebuilt whenever the underlying lists change
/// - the game's own page is where it is edited.
/// </summary>
public class InstanceOverviewViewModel(
    Game game,
    string activeProfileSummary,
    DriftReport? drift = null)
{
    public string Name { get; } = game.Name;

    /// <summary>Joined for now; slice 5 turns this into the target list it really is.</summary>
    public string ModFolder { get; } = game.Targets.Count > 0
        ? string.Join(", ", game.Targets.Select(x => x.ModFolder))
        : "No mod folder configured";

    public string ActiveProfileSummary { get; } = activeProfileSummary;

    /// <summary>
    /// Null where the last check found nothing to say. Drift belongs wherever the game appears,
    /// but a game that matches its profile does not need a line saying so on every list.
    /// </summary>
    public string? DriftSummary { get; } = Describe(drift);

    public bool HasDrift => DriftSummary is not null;


    private static string? Describe(DriftReport? report)
    {
        if (report is not DriftReport drift || drift.Status is not DriftStatus.Drifted)
        {
            return null;
        }

        // The dangerous case gets its own words: an unlocked mod at the wrong version is untidy, a
        // locked map at the wrong version is a damaged savegame waiting to happen.
        if (drift.LockedDrift.Count > 0)
        {
            return $"{drift.DifferenceCount} differences, including the locked '{drift.LockedDrift[0].DisplayName}'. Hosting a savegame may damage it.";
        }

        // A profile that moved on is drift with nothing in the folder to count, so a difference
        // count would read "0 differences" - which is both false and unhelpful.
        var moved = drift.ProfileHasMoved
            ? $"Applied at revision {drift.AppliedRevision}; the profile is now at revision {drift.CurrentRevision}."
            : null;

        if (drift.DifferenceCount == 0)
        {
            return moved ?? "Differs from what was last applied here.";
        }

        var differences = $"{drift.DifferenceCount} differences from what was last applied here.";

        return moved is null ? differences : $"{differences} {moved}";
    }
}
