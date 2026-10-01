using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// What a check-in ended up doing. "Backed out" and "chose to look at theirs first" leave the caller
/// different things to say, so it is more than a nullable snapshot.
/// </summary>
public sealed record SavegameCheckInOutcome(SavegameSnapshotDto? Snapshot, bool KeptPlaying, bool WasDeferred, SavegameLocalCopy LocalCopy = SavegameLocalCopy.Kept)
{
    public static SavegameCheckInOutcome Cancelled { get; } = new(null, false, false);

    /// <summary>The base was stale and the user chose to look at the newer snapshot first.</summary>
    public static SavegameCheckInOutcome Deferred { get; } = new(null, false, true);

    public static SavegameCheckInOutcome CheckedIn(SavegameSnapshotDto snapshot, bool keptPlaying, SavegameLocalCopy localCopy)
        => new(snapshot, keptPlaying, false, localCopy);

    public bool Succeeded => Snapshot is not null;

    /// <summary>Whether the slot is now free, which is what the caller has to re-read the disk about.</summary>
    public bool ReleasedTheSlot => Succeeded && KeptPlaying is false;
}
