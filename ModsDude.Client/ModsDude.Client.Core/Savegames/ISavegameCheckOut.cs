using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Writes a savegame from the server into a slot. Never touches mods; the caller applies the profile.</summary>
public interface ISavegameCheckOut
{
    /// <summary>
    /// Takes the claim on a savegame and writes a snapshot of it into a slot. A snapshot other than
    /// the head is restored as the new head first, so the claim has no stale base.
    /// </summary>
    /// <remarks>
    /// A savegame already held here in another slot moves: its old copy goes to the Recycle Bin once
    /// the new one is bound. Another savegame's clean copy in the slot is given back.
    /// </remarks>
    /// <param name="restoreRequestId">The same for a repeat of this restore, which is then answered as the original was.</param>
    /// <param name="progress">Which stage the bytes are in and how far through it they are.</param>
    /// <exception cref="Exceptions.UserFriendlyException">
    /// The game is running, the slot or this savegame's own copy elsewhere holds play nobody has
    /// checked in, the game already holds a savegame that claims its mod folder, or the savegame
    /// changed since the caller saw it.
    /// </exception>
    Task<SavegameCheckOutResult> CheckOutAsync(
        Game game,
        SavegameDto savegame,
        int snapshotNumber,
        Guid restoreRequestId,
        SavegameSlotRef slot,
        SavegameRevisionMode revisionMode,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null);

    /// <summary>
    /// Writes a named snapshot into a slot without claiming or binding anything. The slot reads as
    /// unrecognised afterwards. Another savegame's clean copy in the slot is given back.
    /// </summary>
    /// <returns>Where the save the slot held before went, or null where it was empty.</returns>
    Task<DisplacedSavegame?> TakeCopyAsync(Game game, SavegameDto savegame, int snapshotNumber, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null);
}


/// <param name="Savegame">The savegame as checked out: its head is the snapshot now in the slot.</param>
/// <param name="TakenFrom">Whoever held the claim until this took it, or null where nobody else did.</param>
/// <param name="Displaced">The save the slot held before, or null where it was empty.</param>
/// <param name="PreviousCopy">
/// What became of this savegame's copy in the slot it moved out of, or null where it did not move.
/// </param>
public sealed record SavegameCheckOutResult(
    SavegameDto Savegame,
    SavegameClaimHolder? TakenFrom,
    DisplacedSavegame? Displaced,
    SavegameLocalCopy? PreviousCopy);
