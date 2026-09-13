using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One of a game's folders as an overview shows it: where it is, and whether it still matches what
/// was applied to it.
/// </summary>
/// <param name="Text">
/// The path, with the folder's name in front of it where the game reaches more than one. A game with
/// a single folder does not have a mod folder <em>called</em> something, so it reads as the bare path
/// it always did.
/// </param>
/// <param name="Drift">
/// What the last check found here, or null where it found nothing worth saying. Per folder, because
/// each of them matches its profile or does not on its own - which is the whole of what a game with
/// three of them changes about this row.
/// </param>
public sealed record GameFolderLine(string Text, string? Drift)
{
    public bool HasDrift => Drift is not null;
}


/// <summary>
/// One game as an overview shows it: which profile it is meant to match, and a line for each folder
/// that has to match it. Read-only and rebuilt whenever the underlying lists change - the game's own
/// page is where it is edited.
/// </summary>
/// <remarks>
/// <b>A line per folder rather than a row per game with one drift sentence on it.</b> A game whose
/// dedicated server drifted and whose MP client did not used to show the first entry the monitor
/// happened to produce, with nothing saying which folder it was about - which is exactly the silence
/// this phase exists to remove.
/// </remarks>
public class GameOverviewViewModel
{
    /// <param name="drift">
    /// Every entry the monitor has for this game, placed onto the folders below by key. Entries
    /// about the game rather than one of its folders - a held savegame in a folder with no mods -
    /// carry no mod drift and land nowhere here, which is right: this row is about mod folders, and
    /// the app-level notice is what says the savegame half.
    /// </param>
    /// <param name="adapter">
    /// The repo's adapter, which is what knows the folders' names. An overview is always under a
    /// repo, so there is always one to ask - unlike the app-level drift notice, which is why the
    /// names are asked for rather than written down.
    /// </param>
    public GameOverviewViewModel(
        Game game,
        IBaseGameAdapter adapter,
        string activeProfileSummary,
        IReadOnlyList<TargetDrift> drift)
    {
        Name = game.Name;
        ActiveProfileSummary = activeProfileSummary;

        var names = TargetNames.Read(game, adapter);

        Folders = [.. game.Targets.Select(target => new GameFolderLine(
            Describe(target, names.GetValueOrDefault(target.Key), game.Targets.Count),
            Describe(drift.FirstOrDefault(x => x.Target?.Target.Key == target.Key)?.Report)))];
    }


    public string Name { get; }

    /// <summary>Empty for a game whose settings point at no folder at all, which is an ordinary answer.</summary>
    public IReadOnlyList<GameFolderLine> Folders { get; }

    public bool HasFolders => Folders.Count > 0;

    /// <summary>The pair, because the sidebar's converters only go one way. Same idiom as HasNoGames.</summary>
    public bool HasNoFolders => Folders.Count == 0;

    public string ActiveProfileSummary { get; }


    private static string Describe(PersistedModTarget target, string? displayName, int targetCount)
        => TargetNames.Distinguishing(target.Key, displayName, targetCount) is string name
            ? $"{name}: {target.ModFolder}"
            : target.ModFolder;

    /// <summary>
    /// Null where the last check found nothing to say. Drift belongs wherever the game appears,
    /// but a folder that matches its profile does not need a line saying so on every list.
    /// </summary>
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
