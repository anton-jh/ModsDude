using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckInFlow
{
    /// <summary>
    /// Asks, uploads, and turns a stale base into a choice rather than an error.
    /// </summary>
    /// <param name="savegameName">What the modal calls the save. Display text only.</param>
    /// <param name="renameTo">
    /// What to write into the slot as the save's name before packing it, or null to leave it alone.
    /// Separate from <paramref name="savegameName"/>, which may be a placeholder that must not land in
    /// the save file.
    /// </param>
    Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
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
