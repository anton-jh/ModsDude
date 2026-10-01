using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Hands a held savegame back, from the slot its binding names.</summary>
public interface ISavegameCheckIn
{
    /// <summary>Hands a held savegame back, minting a snapshot from whatever is in its slot now.</summary>
    /// <remarks>
    /// A stale base, and somebody else holding the claim of a save being kept, come back as the
    /// server's own <see cref="ApiException{TResult}"/>, so the caller can ask whether to force past it
    /// or take the claim. Every failure up to the commit leaves the folder as it was, and a check-in of
    /// the same bytes afterwards repeats the same request, so the whole thing can be retried.
    /// </remarks>
    /// <param name="keepPlaying">Keeps the save checked out, rebased onto the snapshot just minted.</param>
    /// <param name="takeOver">With <paramref name="keepPlaying"/>, takes the claim from whoever holds it.</param>
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
        bool takeOver,
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


/// <param name="HoldsClaim">Whether this machine still holds the save, which is the server's answer rather than what was asked for.</param>
/// <param name="TakenFrom">The claim taken from somebody else to keep playing, or null where none was.</param>
public sealed record SavegameCheckInResult(
    SavegameSnapshotDto Snapshot,
    bool HoldsClaim,
    SavegameLocalCopy LocalCopy,
    SavegameCheckoutDto? TakenFrom);
