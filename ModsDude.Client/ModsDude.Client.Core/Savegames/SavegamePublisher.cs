using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegamePublisher(
    ISavegamesClient savegamesClient,
    ISavegamePacker packer,
    ISavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    ISavegameSlots slots,
    ISavegameTransfer transfer,
    ISavegameRecycler recycler,
    ISavegameRenamer renamer,
    ISavegameHolds holds,
    IGameRunningGuard runningGuard,
    TimeProvider time,
    ILogger<SavegamePublisher> logger)
    : ISavegamePublisher
{
    /// <remarks>
    /// The id is minted here rather than by the server, because the blob is addressed by
    /// <c>{repoId}/{savegameId}/{contentHash}</c> and has to be uploaded before the savegame exists.
    /// </remarks>
    public async Task<SavegamePublishResult> PublishAsync(
        Game game,
        Guid repoId,
        SavegameSlotRef slot,
        string name,
        string? label,
        SavegamePublishTarget? target,
        bool keepPlaying,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var savegameTarget = adapter.RequireTarget(game, slot);
        var savegameId = Guid.NewGuid();

        // Publishing opens a claim, so it leaves this game holding the savegame.
        bindings.EnsureModFolderIsFree(game, savegameId, target?.ProfileId, name);

        // Nothing has been observed for this savegame yet, so a rename here cannot be mistaken for play.
        renamer.TryRename(adapter, savegameTarget, slot.Slot, name);

        var packed = await packer.PackAsync(adapter, savegameTarget, slot.Slot, ct, progress);

        var details = await slots.ReadDetailsAsync(adapter, savegameTarget, slot.Slot, ct);

        SavegameDto savegame;

        try
        {
            await transfer.UploadAsync(repoId, savegameId, packed, progress, ct);

            progress?.Report(new SavegameProgress(SavegameStage.Recording, 0, 0));

            savegame = await savegamesClient.PublishSavegameV1Async(repoId, new PublishSavegameRequest
            {
                SavegameId = savegameId,
                Name = name,
                ProfileId = target?.ProfileId,
                ProfileRevision = target?.Revision,
                ContentHash = packed.ContentHash,
                SizeBytes = packed.SizeBytes,
                Label = label,
                Details = details
            }, ct);
        }
        finally
        {
            FileSystemHelper.TryDeleteFile(packed.FilePath, logger);
        }

        // Written even where the save is handed straight back: the server opened a claim, and a release
        // that fails must leave a binding to check in or discard rather than a claim nothing remembers.
        bindings.SetBinding(game.Identity, new SavegameCheckoutBinding(
            repoId,
            savegameId,
            slot,
            savegame.Head?.Number ?? 1,
            packed.ContentHash,
            time.GetUtcNow().UtcDateTime)
        {
            ProfileId = target?.ProfileId,
            ProfileRevision = target?.Revision,
            // A newly published savegame follows head.
            TargetRevision = null,
            LastObservedHash = packed.ContentHash,
            LastPlayedRevision = null
        });

        if (keepPlaying is false)
        {
            await holds.ReleaseAsync(game, repoId, savegameId, ct);

            return new SavegamePublishResult(savegame, await recycler.HandBackAsync(adapter, savegameTarget, slot.Slot, packed.ContentHash));
        }

        return new SavegamePublishResult(savegame, SavegameLocalCopy.Kept);
    }
}
