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
/// One game as an overview shows it: which profile it is meant to match, what it is holding, and a
/// line for each folder that has to match it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A line per folder rather than a row per game with one drift sentence on it.</b> A game whose
/// dedicated server drifted and whose MP client did not used to show the first entry the monitor
/// happened to produce, with nothing saying which folder it was about - which is exactly the silence
/// Phase 10 exists to remove.
/// </para>
/// <para>
/// <b>This is where a game's state is read now.</b> It used to be the sidebar of the game's own page,
/// which is gone: a local installation is a settings entry, not a place. So the folder lines say
/// every drift status rather than only <see cref="DriftStatus.Drifted"/> - an apply that never landed
/// and a folder that was repointed are both things somebody has to be able to find out about, and
/// this is the only list left that could tell them.
/// </para>
/// <para>
/// Read-only apart from deactivating. Everything else on it is acted on somewhere else: the profile
/// page activates, the repo's Saves list hands a savegame back, and <em>Configure game</em> is where
/// the folders are edited. Deactivating is here as well because it is the one act that is about the
/// game rather than about a profile - a game set to another repo's profile has no profile page in this
/// repo to do it from.
/// </para>
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
    /// <param name="holdingSummary">
    /// What this game is holding, or null - nearly always - where it is holding nothing with a mod
    /// list. Neutral on purpose: playing a past savegame is a state somebody chose, not a problem
    /// with the game, so it reads like the folder paths rather than like a drift warning.
    /// </param>
    public GameOverviewViewModel(
        Game game,
        IBaseGameAdapter adapter,
        string activeProfileSummary,
        string? holdingSummary,
        IReadOnlyList<TargetDrift> drift)
    {
        Game = game;
        Name = game.Name;
        ActiveProfileSummary = activeProfileSummary;
        HoldingSummary = holdingSummary;

        var names = TargetNames.Read(game, adapter);

        Folders = [.. game.Targets.Select(target => new GameFolderLine(
            Describe(target, names.GetValueOrDefault(target.Key), game.Targets.Count),
            Describe(drift.FirstOrDefault(x => x.Target?.Target.Key == target.Key)?.Report)))];
    }


    /// <summary>The game this row is about, for the one thing a row acts on: deactivating it.</summary>
    public Game Game { get; }

    /// <summary>
    /// Whether the game follows a profile at all - the only state in which there is something to
    /// deactivate. It follows one in another repo just as well: deactivating is about the game, and the
    /// row says whose profile it is.
    /// </summary>
    public bool HasActiveProfile => Game.ActiveProfile is not null;

    public string Name { get; }

    /// <summary>Empty for a game whose settings point at no folder at all, which is an ordinary answer.</summary>
    public IReadOnlyList<GameFolderLine> Folders { get; }

    public bool HasFolders => Folders.Count > 0;

    /// <summary>The pair, because the sidebar's converters only go one way. Same idiom as HasNoGames.</summary>
    public bool HasNoFolders => Folders.Count == 0;

    public string ActiveProfileSummary { get; }

    /// <summary>What this game is holding, or null where it holds nothing with a mod list.</summary>
    public string? HoldingSummary { get; }

    public bool HasHoldingSummary => HoldingSummary is not null;


    private static string Describe(PersistedModTarget target, string? displayName, int targetCount)
        => TargetNames.Distinguishing(target.Key, displayName, targetCount) is string name
            ? $"{name}: {target.ModFolder}"
            : target.ModFolder;

    /// <summary>
    /// What the last check found in this folder, or null where it found nothing worth saying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All four statuses the monitor reports, not only <see cref="DriftStatus.Drifted"/>.</b> The
    /// game's own page used to say the other three, and there is no game page: an intent that was
    /// never carried out and a folder that was pointed somewhere else are exactly the states somebody
    /// opens an overview to find, and saying only the first would leave them to the app-level notice
    /// alone - which is a notice that can be dismissed.
    /// </para>
    /// <para>
    /// <b>Four and not seven</b>, because the rows come from <see cref="DriftMonitor.Drifted"/> and
    /// that is what it carries. Nothing is lost: a folder that matches needs no line, and a game with
    /// no profile or a profile that is gone is already said once by
    /// <see cref="ActiveProfileSummary"/> - saying it again per folder would be the same news three
    /// times for a game reaching three of them.
    /// </para>
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
            // Three ways for an intent to stand with no work behind it, and they are three different
            // sentences: an apply that did not land, a folder nothing was ever applied to, and a
            // settings edit somebody made a moment ago.
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
