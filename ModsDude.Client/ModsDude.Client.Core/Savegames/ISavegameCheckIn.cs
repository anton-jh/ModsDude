using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Hands a held savegame back, from the slot its binding names.</summary>
public interface ISavegameCheckIn
{
    /// <summary>Hands a held savegame back, minting a snapshot from whatever is in its slot now.</summary>
    /// <remarks>
    /// A stale base comes back as the server's own <see cref="ApiException{TResult}"/>, so the caller can
    /// ask whether to force past it. Every failure up to the commit leaves the binding and the folder as
    /// they were, so the whole thing can be retried.
    /// </remarks>
    /// <param name="keepPlaying">Keeps the save checked out, rebased onto the snapshot just minted.</param>
    /// <param name="progress">Which stage the bytes are in and how far through it they are.</param>
    /// <param name="savegameName">
    /// What to rename the slot to before packing it, or null to leave the name as it is.
    /// </param>
    Task<SavegameCheckInResult> CheckInAsync(
        Game game,
        Guid savegameId,
        string? label,
        bool keepPlaying,
        bool force,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null,
        string? savegameName = null);

    /// <summary>
    /// Gives a savegame back without minting a snapshot, and recycles the local copy. For a check-out
    /// taken by mistake.
    /// </summary>
    /// <returns>
    /// Whether the local copy reached the Recycle Bin. The claim is released either way.
    /// </returns>
    Task<bool> DiscardAsync(Game game, Guid savegameId, CancellationToken ct);
}


public sealed record SavegameCheckInResult(SavegameSnapshotDto Snapshot, SavegameLocalCopy LocalCopy);
