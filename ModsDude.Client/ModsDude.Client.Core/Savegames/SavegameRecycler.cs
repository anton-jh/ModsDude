using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameRecycler(
    IRecycleBin recycleBin,
    IContentStoreProvider storeProvider,
    ISavegamePacker packer,
    TimeProvider time,
    ILogger<SavegameRecycler> logger)
    : ISavegameRecycler
{
    /// <summary>How long to wait before each further attempt at recycling a slot. Settable so tests do not wait.</summary>
    internal IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(2)];


    public DisplacedSavegame PutAway(string displacedPath)
    {
        var name = Path.GetFileName(displacedPath);

        if (recycleBin.TryRecycle(displacedPath) && Directory.Exists(displacedPath) is false)
        {
            return new DisplacedSavegame(DisplacedSavegameDestination.RecycleBin, name, null);
        }

        try
        {
            var quarantine = storeProvider.GetStoreServing(displacedPath).GetQuarantineDirectory(time.GetUtcNow());

            Directory.CreateDirectory(quarantine);

            var destination = FileSystemHelper.GetUnusedPath(quarantine, name);

            FileSystemHelper.MoveDirectory(displacedPath, destination, logger);

            return new DisplacedSavegame(DisplacedSavegameDestination.QuarantineFolder, name, destination);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not recycle or quarantine the replaced save {Path}; it stays beside the slots.", displacedPath);

            return new DisplacedSavegame(DisplacedSavegameDestination.BesideSlots, name, displacedPath);
        }
    }

    /// <remarks>
    /// Not cancellable: it runs after the claim is handed back, and stopping halfway through would
    /// leave nobody to say where the copy went.
    /// </remarks>
    public async Task<bool> RecycleSlotAsync(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot)
    {
        try
        {
            var path = adapter.GetSlotPath(target, slot);

            for (var attempt = 0; ; attempt++)
            {
                if (Directory.Exists(path) is false)
                {
                    return true;
                }

                // There the shell's only way to "recycle" is to delete, and a copy left in the slot is
                // recoverable where a deleted one is not.
                if (recycleBin.IsAvailableFor(path) is false)
                {
                    logger.LogWarning("Slot {Slot} was not recycled: {Path} is on a volume with no Recycle Bin.", slot.Value, path);

                    return false;
                }

                if (recycleBin.TryRecycle(path) && Directory.Exists(path) is false)
                {
                    return true;
                }

                if (attempt == RetryDelays.Count)
                {
                    break;
                }

                await Task.Delay(RetryDelays[attempt], time, CancellationToken.None);
            }

            logger.LogWarning(
                "Could not move slot {Slot} to the Recycle Bin after handing it back, in {Attempts} attempts; it is still at {Path}.",
                slot.Value, RetryDelays.Count + 1, path);

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not clear slot {Slot} after handing it back.", slot.Value);

            return false;
        }
    }

    /// <remarks>
    /// Never throws: by the time this runs the snapshot is committed and the claim released, and a
    /// hand-back that succeeded must not be reported as failing over its last, local step.
    /// </remarks>
    public async Task<SavegameLocalCopy> HandBackAsync(
        ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string uploadedHash)
    {
        try
        {
            if (Directory.Exists(adapter.GetSlotPath(target, slot)))
            {
                var current = await packer.HashSlotAsync(adapter, target, slot, CancellationToken.None);

                if (ModContentHasher.Matches(current, uploadedHash) is false)
                {
                    logger.LogWarning("Slot {Slot} changed after it was uploaded; it was left where it is rather than recycled.", slot.Value);

                    return SavegameLocalCopy.ChangedSinceUpload;
                }
            }

            return await RecycleSlotAsync(adapter, target, slot)
                ? SavegameLocalCopy.Recycled
                : SavegameLocalCopy.LeftBehind;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not hand slot {Slot} back; it was left where it is.", slot.Value);

            return SavegameLocalCopy.LeftBehind;
        }
    }
}
