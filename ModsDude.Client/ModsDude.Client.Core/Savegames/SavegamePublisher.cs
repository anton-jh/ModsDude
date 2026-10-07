using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegamePublisher(
    ISavegamesClient savegamesClient,
    ISavegameStore store,
    ISavegamePacker packer,
    ISavegameBindingStore bindings,
    ISavegamePendingPublishes pendingPublishes,
    IHeldSavegames heldSavegames,
    ILocalSavegameAdapters adapters,
    ISavegameSlots slots,
    ISavegameTransfer transfer,
    ISavegameRecycler recycler,
    ISavegameRenamer renamer,
    IGameRunningGuard runningGuard,
    TimeProvider time,
    ILogger<SavegamePublisher> logger)
    : ISavegamePublisher
{
    /// <remarks>
    /// The savegame id is minted here rather than by the server, because the blob is addressed by
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

        if (keepPlaying)
        {
            EnsureCanKeep(game, repoId, target?.ProfileId, name);
        }

        var adapter = adapters.Require(game);
        var savegameTarget = adapter.RequireTarget(game, slot);

        // Nothing has been observed for this savegame yet, so a rename here cannot be mistaken for play.
        renamer.TryRename(adapter, savegameTarget, slot.Slot, name);

        var packed = await packer.PackAsync(adapter, savegameTarget, slot.Slot, ct, progress);

        var details = await slots.ReadDetailsAsync(adapter, savegameTarget, slot.Slot, ct);

        // The same bytes as an unanswered publish are the same publish, so it is repeated under its ids
        // and the server answers it as it did the first time.
        var pending = pendingPublishes.Begin(game.Identity, slot, packed.ContentHash);

        PublishSavegameResponse response;

        try
        {
            await transfer.UploadAsync(repoId, pending.SavegameId, packed, progress, ct);

            progress?.Report(new SavegameProgress(SavegameStage.Recording, 0, 0));

            response = await store.WriteAsync(repoId, token => savegamesClient.PublishSavegameV1Async(repoId, new PublishSavegameRequest
            {
                RequestId = pending.RequestId,
                SavegameId = pending.SavegameId,
                Name = name,
                ProfileId = target?.ProfileId,
                ProfileRevision = target?.Revision,
                ContentHash = packed.ContentHash,
                SizeBytes = packed.SizeBytes,
                Label = label,
                Details = details,
                KeepPlaying = keepPlaying
            }, token), ct);
        }
        finally
        {
            FileSystemHelper.TryDeleteFile(packed.FilePath, logger);
        }

        var savegame = response.Savegame;

        // The server's answer rather than the request's: a repeat is answered as the first one was,
        // whatever it asked for this time. The pending publish is forgotten last, so a crash before
        // then repeats it rather than publishing the slot a second time.
        if (response.HoldsClaim)
        {
            bindings.SetBinding(game.Identity, new SavegameCheckoutBinding(
                repoId,
                savegame.Id,
                slot,
                savegame.Head?.Number ?? 1,
                packed.ContentHash,
                time.GetUtcNow().UtcDateTime)
            {
                ProfileId = savegame.ProfileId,
                ProfileRevision = savegame.Head?.ProfileRevision,
                // A newly published savegame follows head.
                TargetRevision = null,
                LastObservedHash = packed.ContentHash,
                LastPlayedRevision = null
            });

            pendingPublishes.Complete(game.Identity, pending.RequestId);

            return new SavegamePublishResult(savegame, SavegameLocalCopy.Kept);
        }

        var localCopy = await recycler.HandBackAsync(adapter, savegameTarget, slot.Slot, packed.ContentHash);

        pendingPublishes.Complete(game.Identity, pending.RequestId);

        return new SavegamePublishResult(savegame, localCopy);
    }

    private void EnsureCanKeep(Game game, Guid repoId, Guid? profileId, string savegameName)
    {
        var plan = heldSavegames.DecideKeepPublished(game, repoId, profileId);

        if (plan.ChecksInFirst is Guid blocking)
        {
            throw new UserFriendlyException(
                $"'{savegameName}' cannot stay checked out",
                $"Game '{game.Identity}' already holds savegame '{blocking}', which follows a profile, so its mod folder is spoken for.");
        }

        if (plan.ActivatesFirst)
        {
            throw new UserFriendlyException(
                $"'{savegameName}' cannot stay checked out",
                $"Game '{game.Identity}' is not on profile '{profileId}', so a savegame following it cannot be held there.");
        }
    }
}
