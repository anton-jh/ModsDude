using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckInFlow
{
    /// <summary>The check-in step for a savegame this game holds.</summary>
    /// <param name="savegameName">What the step calls the save. Display text only.</param>
    /// <param name="handBackReason">
    /// Why the save has to be handed back, where the check-in is a step towards something else. Null
    /// where keeping it is a choice.
    /// </param>
    SavegameCheckInStepViewModel CreateStep(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        string? handBackReason = null);

    /// <summary>
    /// Uploads what the slot holds, and turns a stale base or somebody else's claim into a choice
    /// rather than an error. A refusal is reported here and comes back as cancelled.
    /// </summary>
    /// <param name="savegameName">What any question about it calls the save. Display text only.</param>
    /// <param name="renameTo">
    /// What to write into the slot as the save's name before packing it, or null to leave it alone.
    /// Separate from <paramref name="savegameName"/>, which may be a placeholder that must not land in
    /// the save file.
    /// </param>
    Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string? label,
        bool keepPlaying,
        CancellationToken cancellationToken,
        string? renameTo = null);

    /// <summary>
    /// Checks a save in from the game holding it, says what happened in a toast, and re-runs the drift
    /// check. Failures are reported here.
    /// </summary>
    /// <param name="changed">Called once a snapshot landed, so the caller can re-read.</param>
    Task CheckInHeldAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        Func<Task> changed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands the claim back without minting anything. The local copy goes to the Recycle Bin.
    /// </summary>
    /// <returns>False where the user backed out.</returns>
    Task<bool> DiscardAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        bool hasUnpublishedPlay,
        CancellationToken cancellationToken);
}
