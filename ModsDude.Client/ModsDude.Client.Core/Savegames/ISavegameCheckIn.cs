using ModsDude.Client.Core.Exceptions;
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
    /// the same contents afterwards repeats the same request, so the whole thing can be retried.
    /// </remarks>
    /// <param name="keepPlaying">Keeps the save checked out, rebased onto the snapshot just minted.</param>
    /// <param name="takeOver">With <paramref name="keepPlaying"/>, takes the claim from whoever holds it.</param>
    /// <param name="progress">Which stage the bytes are in and how far through it they are.</param>
    /// <param name="savegameName">
    /// The name the snapshot carries, or null to leave it as it is. The slot itself takes it once the
    /// snapshot is accepted, where the save stays checked out.
    /// </param>
    /// <exception cref="UserFriendlyException">
    /// The game is running, the slot is empty, the save's mod list is unknown, or a lost earlier
    /// check-in turns out to have handed it back already.
    /// </exception>
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
    /// <returns>What became of the local copy. The claim is released either way.</returns>
    Task<SavegameLocalCopy> DiscardAsync(Game game, Guid savegameId, CancellationToken ct);
}


/// <param name="HoldsClaim">Whether this machine still holds the save, which is the server's answer rather than what was asked for.</param>
/// <param name="TakenFrom">The claim taken from somebody else to keep playing, or null where none was.</param>
public sealed record SavegameCheckInResult(
    SavegameSnapshotDto Snapshot,
    bool HoldsClaim,
    SavegameLocalCopy LocalCopy,
    SavegameCheckoutDto? TakenFrom);
