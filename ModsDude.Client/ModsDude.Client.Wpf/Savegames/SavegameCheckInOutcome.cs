using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// What a check-in ended up doing. "Backed out" and "chose to look at theirs first" leave the caller
/// different things to say, so it is more than a nullable snapshot.
/// </summary>
/// <param name="KeptPlaying">Whether this machine still holds the save, as the server answered.</param>
/// <param name="TakenFrom">The claim taken from somebody else to keep playing, or null where none was.</param>
public sealed record SavegameCheckInOutcome(
    SavegameSnapshotDto? Snapshot,
    bool KeptPlaying,
    bool WasDeferred,
    SavegameLocalCopy LocalCopy = SavegameLocalCopy.Kept,
    SavegameCheckoutDto? TakenFrom = null)
{
    public static SavegameCheckInOutcome Cancelled { get; } = new(null, false, false);

    /// <summary>
    /// The user chose to leave the save as it is: the base was stale and they want to look at the newer
    /// snapshot first, or somebody else holds it and they would not take it from them.
    /// </summary>
    public static SavegameCheckInOutcome Deferred { get; } = new(null, false, true);

    public static SavegameCheckInOutcome CheckedIn(SavegameCheckInResult result)
        => new(result.Snapshot, result.HoldsClaim, false, result.LocalCopy, result.TakenFrom);

    public bool Succeeded => Snapshot is not null;

    /// <summary>Whether the slot is now free, which is what the caller has to re-read the disk about.</summary>
    public bool ReleasedTheSlot => Succeeded && KeptPlaying is false;
}
