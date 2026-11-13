using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// Gets savegame folders out of a game's way without losing them: the Recycle Bin first, and never a
/// plain delete. None of these throw; each says where the folder ended up.
/// </summary>
public interface ISavegameRecycler
{
    /// <summary>
    /// Sends a save a check-out or copy replaced to the Recycle Bin, or failing that into the content
    /// store's quarantine folder. Where both refuse it stays beside the slots, under a name that says
    /// which slot it was replaced in and when.
    /// </summary>
    DisplacedSavegame PutAway(string displacedPath, string slotPath);

    /// <summary>
    /// Puts right what an interrupted write left beside a slot: half-extracted copies are deleted, a
    /// slot that was moved aside and never replaced is moved back, and a slot that was replaced has
    /// its old contents put away. Run before anything else touches the slot.
    /// </summary>
    /// <returns>Where each old copy that was put away went.</returns>
    IReadOnlyList<DisplacedSavegame> RecoverSlot(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot);

    /// <summary>
    /// Sends a slot's folder to the Recycle Bin, asking again briefly while something (a virus scanner,
    /// the search indexer) still holds a file in it. Never on a volume without a Recycle Bin.
    /// </summary>
    /// <returns>Whether the folder is gone: recycled, or never there.</returns>
    Task<bool> RecycleSlotAsync(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot);

    /// <summary>
    /// Recycles a slot that has just been uploaded, but only while it still holds exactly what was
    /// packed. Anything the game wrote since exists nowhere else, so that slot is left alone.
    /// </summary>
    /// <param name="packedSlotHash">What the slot hashed to when it was packed for the upload.</param>
    Task<SavegameLocalCopy> HandBackAsync(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string packedSlotHash);
}


/// <summary>What became of the slot a check-in or publish uploaded from.</summary>
public enum SavegameLocalCopy
{
    /// <summary>Still checked out, as asked.</summary>
    Kept,

    Recycled,

    /// <summary>Handed back, but the folder could not be moved to the Recycle Bin and is still in its slot.</summary>
    LeftBehind,

    /// <summary>Handed back, but the slot changed after the upload, so it was left in its slot.</summary>
    ChangedSinceUpload,

    /// <summary>Handed back, but the game was running by then, so the slot was left alone.</summary>
    LeftGameRunning
}


/// <summary>Where the save a check-out or copy replaced has gone.</summary>
public enum DisplacedSavegameDestination
{
    RecycleBin,

    /// <summary>The Recycle Bin refused it, so it was moved into the content store's quarantine folder.</summary>
    QuarantineFolder,

    /// <summary>Neither worked, so it is still beside the slots, named for the slot it was replaced in.</summary>
    BesideSlots
}


/// <param name="Name">The folder's name now, e.g. <c>savegame3 (replaced 2026-09-30 14-02)</c>.</param>
/// <param name="Path">Where it is, where that is not the Recycle Bin.</param>
public sealed record DisplacedSavegame(DisplacedSavegameDestination Destination, string Name, string? Path);
