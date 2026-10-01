using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.Games;

/// <summary>
/// One of a game's folders as an overview shows it: what it is, where it is, and whether it still
/// matches what was applied to it.
/// </summary>
/// <param name="Drift">What the last check found here, or null where it found nothing worth saying.</param>
public sealed record GameFolderLine(string Label, string Path, string? Drift)
{
    public bool HasDrift => Drift is not null;
}


/// <summary>
/// The game this machine has connected for a repo, as the repo's overview shows it: its folders,
/// which profile it follows, what it is holding, and whether it can be found at all.
/// </summary>
public class GameOverviewViewModel
{
    /// <param name="drift">
    /// Every entry the monitor has for this game, placed onto its mod folders by key. Entries about
    /// the game rather than one of its folders land nowhere here; the app-level notice says those.
    /// </param>
    /// <param name="holdingSummary">What this game is holding, or null where it holds nothing with a mod list.</param>
    public GameOverviewViewModel(
        Game game,
        GameInstallation installation,
        string activeProfileSummary,
        string? holdingSummary,
        IReadOnlyList<TargetDrift> drift)
    {
        Game = game;
        Name = game.Name;
        Problem = installation.Problem;
        ActiveProfileSummary = activeProfileSummary;
        HoldingSummary = holdingSummary;

        Folders = [.. installation.Folders.Select(folder => new GameFolderLine(
            Label(folder),
            folder.Path,
            folder.Kind is GameFolderKind.Mods
                ? Describe(drift.FirstOrDefault(x => x.Target?.Target.Key == folder.Key)?.Report)
                : null))];
    }


    public Game Game { get; }

    /// <summary>Whether the game follows a profile at all - the only state in which there is something to deactivate.</summary>
    public bool HasActiveProfile => Game.ActiveProfile is not null;

    public string Name { get; }

    /// <summary>Why the game cannot be read on this machine right now, or null where it can.</summary>
    public string? Problem { get; }

    public bool HasProblem => Problem is not null;

    public IReadOnlyList<GameFolderLine> Folders { get; }

    public bool HasNoFolders => Folders.Count == 0;

    public string ActiveProfileSummary { get; }

    public string? HoldingSummary { get; }

    public bool HasHoldingSummary => HoldingSummary is not null;


    private static string Label(GameFolder folder)
    {
        var kind = folder.Kind switch
        {
            GameFolderKind.Mods => "Mods",
            GameFolderKind.Savegames => "Saves",
            _ => throw new ArgumentOutOfRangeException(nameof(folder), folder.Kind, null)
        };

        return folder.Name is string name ? $"{kind} ({name})" : kind;
    }

    /// <summary>
    /// What the last check found in this folder, or null where it found nothing worth saying.
    /// </summary>
    /// <remarks>
    /// Every status the monitor reports, not only <see cref="DriftStatus.Drifted"/>: an apply that
    /// never landed and a folder that was repointed are both things somebody opens an overview to
    /// find. A game with no profile, or one that is gone, is already said by
    /// <see cref="ActiveProfileSummary"/>.
    /// </remarks>
    private static string? Describe(DriftReport? report)
    {
        if (report is not DriftReport drift)
        {
            return null;
        }

        return drift.Status switch
        {
            DriftStatus.Drifted => DescribeDrifted(drift),
            DriftStatus.NotApplied => drift.AppliedProfileName is string applied
                ? $"Still on '{applied}'. This profile has not been applied here yet."
                : "Still on the profile it was last applied to, not this one.",
            DriftStatus.NeverSynced => "This profile has not been applied here yet.",
            DriftStatus.FolderRepointed => "The settings have been pointed somewhere else, and nothing has been applied there yet.",
            _ => null
        };
    }

    private static string DescribeDrifted(DriftReport drift)
    {
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
