using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Writes a savegame from the server into a slot. Never touches mods; the caller applies the profile.</summary>
public interface ISavegameCheckOut
{
    /// <summary>Takes the claim on a savegame and writes its head snapshot into a slot.</summary>
    /// <param name="progress">Which stage the bytes are in and how far through it they are.</param>
    /// <exception cref="Exceptions.UserFriendlyException">
    /// The game is running, the slot holds play nobody has checked in, or the game already holds a
    /// savegame that claims its mod folder.
    /// </exception>
    Task<SavegameCheckOutResult> CheckOutAsync(Game game, SavegameDto savegame, SavegameSlotRef slot, SavegameRevisionMode revisionMode, CancellationToken ct, IProgress<SavegameProgress>? progress = null);

    /// <summary>Copies an older snapshot forward as the new head, so it can be checked out.</summary>
    /// <returns>The savegame as the caller saw it, with the restored snapshot as its head.</returns>
    /// <exception cref="Exceptions.UserFriendlyException">The head is no longer the one the caller saw.</exception>
    Task<SavegameDto> RestoreAsync(SavegameDto savegame, int snapshotNumber, CancellationToken ct);

    /// <summary>
    /// Writes a named snapshot into a slot without claiming or binding anything. The slot reads as
    /// unrecognised afterwards.
    /// </summary>
    /// <returns>Where the save the slot held before went, or null where it was empty.</returns>
    Task<DisplacedSavegame?> TakeCopyAsync(Game game, SavegameDto savegame, int snapshotNumber, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null);
}


/// <param name="TakenFrom">Whoever held the claim until this took it, or null where nobody else did.</param>
/// <param name="Displaced">The save the slot held before, or null where it was empty.</param>
public sealed record SavegameCheckOutResult(SavegameClaimHolder? TakenFrom, DisplacedSavegame? Displaced);
