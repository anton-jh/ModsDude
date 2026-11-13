using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckInFlow
{
    /// <summary>The check-in step for a savegame this game holds.</summary>
    /// <param name="handBackReason">
    /// Why the save has to be handed back, where the check-in is a step towards something else. Null
    /// where keeping it is a choice.
    /// </param>
    SavegameCheckInStepViewModel CreateStep(
        Game game,
        Guid savegameId,
        HeldSavegameName savegame,
        string? handBackReason = null);

    /// <summary>
    /// Uploads what the slot holds, as a step of a savegame operation already running, and turns a stale
    /// base or somebody else's claim into a choice rather than an error. A refusal is reported here and
    /// comes back as cancelled.
    /// </summary>
    /// <param name="savegame">What questions call the save. A known name is also the name the snapshot carries.</param>
    /// <param name="task">The running operation's strip entry, which the check-in reports on.</param>
    Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        HeldSavegameName savegame,
        string? label,
        bool keepPlaying,
        IBackgroundTask task,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks how to check a save in from the game holding it, then does it as a savegame operation of
    /// its own: claimed, on the strip with its Cancel, and carried on wherever the user goes. Says what
    /// happened in a toast and re-runs the drift check. Failures are reported here.
    /// </summary>
    /// <param name="cancellationToken">Ends the question, and nothing after it.</param>
    Task CheckInHeldAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands the claim back without minting anything, as a savegame operation of its own. The local
    /// copy goes to the Recycle Bin.
    /// </summary>
    /// <param name="cancellationToken">Ends the question, and nothing after it.</param>
    /// <returns>Whether it was given back.</returns>
    Task<bool> DiscardAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        CancellationToken cancellationToken);
}
