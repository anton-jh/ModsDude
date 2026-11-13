using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// Runs savegame work as a gesture of its own rather than as part of a page: claimed on the savegame
/// and the game's savegame folders, drawn on the shell strip with its own Cancel, and carried on
/// when the page that started it is navigated away from.
/// </summary>
/// <remarks>
/// The claim is what the exit confirmation, the account switch and the game settings ask about, and
/// what a second gesture on the same save or folder is refused by - see
/// <see cref="Core.Concurrency.ResourceKeys.Savegame"/> and <see cref="Core.Concurrency.ResourceKeys.SavegameFolder"/>.
/// </remarks>
public interface ISavegameOperations
{
    /// <summary>
    /// Claims what the operation touches and runs it to its end, saying how it went: a toast where it
    /// was stopped, the error modal where it broke. A claim somebody else holds is refused with a
    /// modal naming them, and nothing runs.
    /// </summary>
    /// <param name="work">
    /// The operation, handed the strip entry to report on and the token its Cancel stops.
    /// </param>
    /// <returns>Whether the work ran to its end.</returns>
    Task<bool> RunAsync(SavegameOperation operation, Func<IBackgroundTask, CancellationToken, Task> work);

    /// <summary>Whether savegame work is running on this savegame, or in any of the game's savegame folders.</summary>
    bool IsBusy(Game game, Guid? savegameId = null);

    /// <summary>What is running there, named for somebody waiting on it. Null where nothing is.</summary>
    string? DescribeBusy(Game game, Guid? savegameId = null);
}


/// <summary>One piece of savegame work, as the strip and its endings describe it.</summary>
/// <param name="Game">The game whose savegames it touches.</param>
/// <param name="Title">What is happening, for the strip and for anybody told it is busy: "Checking 'Season 4' in".</param>
/// <param name="Doing">What the error modal says was being done: "checking a savegame in".</param>
/// <param name="Stopped">What a toast says once Cancel has stopped it, including what was left where.</param>
/// <param name="Savegames">The savegames it claims.</param>
/// <param name="AllFolders">
/// Whether it claims every savegame folder of the game - a gesture of several steps - or only the
/// folders of the savegames it names.
/// </param>
public sealed record SavegameOperation(
    Game Game,
    string Title,
    string Doing,
    string Stopped,
    IReadOnlyList<Guid> Savegames,
    bool AllFolders)
{
    public string? Detail { get; init; }
}
