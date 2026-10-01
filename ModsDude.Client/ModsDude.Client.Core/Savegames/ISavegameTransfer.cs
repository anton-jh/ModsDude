using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Moves savegame bytes between storage and a slot.</summary>
public interface ISavegameTransfer
{
    /// <summary>
    /// Fetches a snapshot's blob, verifies it against the hash it was asked for, and replaces the slot's
    /// contents with it.
    /// </summary>
    /// <returns>Where the slot's previous contents went, or null where it was empty.</returns>
    Task<DisplacedSavegame?> DownloadIntoSlotAsync(
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        Guid repoId,
        Guid savegameId,
        string contentHash,
        SavegameSlotId slot,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct);

    /// <summary>
    /// Puts a packed savegame in storage, or establishes that the same bytes are already there.
    /// </summary>
    Task UploadAsync(Guid repoId, Guid savegameId, PackedSavegame packed, IProgress<SavegameProgress>? progress, CancellationToken ct);
}
