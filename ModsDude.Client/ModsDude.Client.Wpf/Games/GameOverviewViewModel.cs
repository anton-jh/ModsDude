using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.Games;

/// <summary>
/// One label/value row of a game as an overview shows it: the profile it follows, or one of its
/// folders.
/// </summary>
/// <param name="Note">What is wrong with it, under the value, or null where nothing is.</param>
/// <param name="IsProblem">Whether the value itself is the problem, and is drawn as one.</param>
public sealed record GameDetailLine(string Label, string Value, string? Note = null, bool IsProblem = false)
{
    /// <summary>What a drifted mod folder differs by, under the note.</summary>
    public IReadOnlyList<NoticeFact> Facts { get; init; } = [];

    public bool HasNote => Note is not null;
    public bool HasFacts => Facts.Count > 0;
}


/// <summary>
/// The game this machine has connected for a repo, as the repo's overview shows it: which profile it
/// follows, its folders, what it is holding, and whether it can be found at all.
/// </summary>
public class GameOverviewViewModel
{
    /// <param name="drift">
    /// Every entry the monitor has for this game, placed onto its mod folders by key. Entries about
    /// the game rather than one of its folders land nowhere here; the app-level notice says those.
    /// </param>
    /// <param name="activeProfile">The profile row, or null on a page that is about the profile already.</param>
    /// <param name="holdingSummary">What this game is holding, or null where it holds nothing with a mod list.</param>
    public GameOverviewViewModel(
        Game game,
        GameInstallation installation,
        GameDetailLine? activeProfile,
        string? holdingSummary,
        IReadOnlyList<TargetDrift> drift)
    {
        Game = game;
        Name = game.Name;
        Problem = installation.Problem;
        HoldingSummary = holdingSummary;

        Folders = [.. installation.Folders.Select(folder =>
        {
            var report = folder.Kind is GameFolderKind.Mods
                ? drift.FirstOrDefault(x => x.Target?.Target.Key == folder.Key)?.Report
                : null;

            return new GameDetailLine(Label(folder), folder.Path, Describe(report))
            {
                Facts = report is { Status: DriftStatus.Drifted } drifted ? DriftFacts.For(drifted) : []
            };
        })];

        Details = activeProfile is null ? Folders : [activeProfile, .. Folders];
    }


    public Game Game { get; }

    /// <summary>Whether the game follows a profile at all - the only state in which there is something to deactivate.</summary>
    public bool HasActiveProfile => Game.ActiveProfile is not null;

    public string Name { get; }

    /// <summary>Why the game cannot be read on this machine right now, or null where it can.</summary>
    public string? Problem { get; }

    public bool HasProblem => Problem is not null;

    public IReadOnlyList<GameDetailLine> Folders { get; }

    public bool HasNoFolders => Folders.Count == 0;

    /// <summary>The profile row, where there is one, then one row per folder.</summary>
    public IReadOnlyList<GameDetailLine> Details { get; }

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
    /// What the last check found in this folder, or null where it found nothing worth saying. A
    /// drifted folder lists what it differs by in <see cref="GameDetailLine.Facts"/>.
    /// </summary>
    /// <remarks>
    /// Every status the monitor reports, not only <see cref="DriftStatus.Drifted"/>: an apply that
    /// never landed and a folder that was repointed are both things somebody opens an overview to
    /// find. A game with no profile, or one that is gone, is already said by its profile row.
    /// </remarks>
    private static string? Describe(DriftReport? report) => report?.Status switch
    {
        DriftStatus.Drifted => report.LockedDrift.Count > 0
            ? "Includes locked mods. Hosting a savegame may damage it."
            : "Differs from what was last applied here.",
        DriftStatus.NotApplied => report.AppliedProfileName is string applied
            ? $"Still on '{applied}'. This profile has not been applied here yet."
            : "Still on the profile it was last applied to, not this one.",
        DriftStatus.NeverSynced => "This profile has not been applied here yet.",
        DriftStatus.FolderRepointed => "The settings have been pointed somewhere else, and nothing has been applied there yet.",
        _ => null
    };
}
