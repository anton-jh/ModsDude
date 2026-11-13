using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
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
    /// <para>
    /// The savegame id is minted here rather than by the server, because the blob is addressed by
    /// <c>{repoId}/{savegameId}/{contentHash}</c> and has to be uploaded before the savegame exists.
    /// It is also what finds out whether a publish whose answer was lost landed after all.
    /// </para>
    /// <para>
    /// The name is packed into the archived copy only, and reaches the slot once the server has the
    /// savegame and the save stays checked out.
    /// </para>
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

        recycler.RecoverSlot(adapter, savegameTarget, slot.Slot);

        await EnsurePublishableAsync(game, slot, ct);

        var rename = renamer.Plan(adapter, savegameTarget, slot.Slot, name);
        var packed = await packer.PackAsync(adapter, savegameTarget, slot.Slot, rename, ct, progress);

        SavegamePendingPublish pending;
        PublishSavegameResponse response;

        try
        {
            if (packed.EntryCount == 0)
            {
                throw SavegameRequirements.SlotIsEmpty(game, slot);
            }

            var details = await slots.ReadDetailsAsync(adapter, savegameTarget, slot.Slot, ct);

            if (pendingPublishes.Find(game.Identity, slot) is SavegamePendingPublish unanswered
                && (unanswered.RepoId != repoId || unanswered.ContentHash != packed.ContentHash))
            {
                await SettleAsync(game, adapter, savegameTarget, unanswered, ct);
            }

            // The same contents as an unanswered publish are the same publish, so it is repeated under
            // its ids and the server answers it as it did the first time.
            pending = pendingPublishes.Begin(game.Identity, slot, repoId, packed.ContentHash, packed.SlotHash);

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
            var slotHash = ApplyRename(game, adapter, savegameTarget, slot.Slot, rename) ? packed.ContentHash : packed.SlotHash;

            Bind(game, repoId, slot, savegame, slotHash);

            pendingPublishes.Complete(game.Identity, pending.RequestId);

            return new SavegamePublishResult(savegame, SavegameLocalCopy.Kept);
        }

        var localCopy = await HandBackAsync(game, adapter, savegameTarget, slot.Slot, packed.SlotHash);

        pendingPublishes.Complete(game.Identity, pending.RequestId);

        return new SavegamePublishResult(savegame, localCopy);
    }

    /// <summary>
    /// Only a save ModsDude has no record of is published: an empty slot has nothing to publish, and a
    /// checked-out save is checked in rather than published a second time.
    /// </summary>
    private async Task EnsurePublishableAsync(Game game, SavegameSlotRef slot, CancellationToken ct)
    {
        switch (await slots.ClassifySlotAsync(game, slot, ct))
        {
            case SavegameSlotAvailability.Free:
                throw SavegameRequirements.SlotIsEmpty(game, slot);

            case SavegameSlotAvailability.HeldClean or SavegameSlotAvailability.HeldWithUnpublishedPlay:
                throw new UserFriendlyException(
                    "That slot holds a checked-out savegame",
                    $"Slot '{slot}' of game '{game.Identity}' is bound to a savegame already, so it is checked in rather than published again.")
                {
                    Hint = "Check that one in instead."
                };
        }
    }

    /// <summary>
    /// Finds out what became of a publish from this slot whose answer was lost, and finishes it here:
    /// the savegame it made is bound where its claim is still this user's, and handed back where not.
    /// </summary>
    /// <exception cref="UserFriendlyException">
    /// It landed, and the slot is now checked out as that savegame or was handed back with it.
    /// </exception>
    private async Task SettleAsync(Game game, ILocalSavegameAdapter adapter, SavegameTarget savegameTarget, SavegamePendingPublish unanswered, CancellationToken ct)
    {
        await store.RefreshAsync(unanswered.RepoId, ct);
        await store.RefreshArchivedAsync(unanswered.RepoId, ct);

        if (store.Find(unanswered.RepoId, unanswered.SavegameId) is not SavegameDto published)
        {
            logger.LogInformation(
                "Unanswered publish {Request} from slot {Slot} never landed; a new one is sent in its place.",
                unanswered.RequestId, unanswered.Slot);

            pendingPublishes.Complete(game.Identity, unanswered.RequestId);

            return;
        }

        logger.LogInformation(
            "Unanswered publish {Request} from slot {Slot} landed as savegame {Savegame}; it is finished here.",
            unanswered.RequestId, unanswered.Slot, published.Id);

        if (store.GetClaim(unanswered.RepoId, published.Id) is { IsYours: true })
        {
            // What the slot held when it was packed is what matched the published snapshot, so any
            // play since reads as play not yet checked in - which it is.
            Bind(game, unanswered.RepoId, unanswered.Slot, published, unanswered.SlotHash);

            pendingPublishes.Complete(game.Identity, unanswered.RequestId);

            throw new UserFriendlyException(
                $"'{published.Name}' was already published",
                $"Publish {unanswered.RequestId} from slot '{unanswered.Slot}' landed as savegame '{published.Id}' and kept it checked out, but its answer was lost.")
            {
                Hint = "It is checked out to you now. Check it in instead."
            };
        }

        var localCopy = await HandBackAsync(game, adapter, savegameTarget, unanswered.Slot.Slot, unanswered.SlotHash);

        pendingPublishes.Complete(game.Identity, unanswered.RequestId);

        // Played on since: what is in the slot now is a save of its own, and publishing it is what was asked.
        if (localCopy is SavegameLocalCopy.ChangedSinceUpload)
        {
            return;
        }

        throw new UserFriendlyException(
            $"'{published.Name}' was already published",
            $"Publish {unanswered.RequestId} from slot '{unanswered.Slot}' landed as savegame '{published.Id}' and handed the save back, but its answer was lost.");
    }

    private void Bind(Game game, Guid repoId, SavegameSlotRef slot, SavegameDto savegame, string slotHash)
    {
        bindings.Bind(game.Identity, new SavegameCheckoutBinding(
            repoId,
            savegame.Id,
            slot,
            savegame.Head?.Number ?? 1,
            slotHash,
            time.GetUtcNow().UtcDateTime)
        {
            ProfileId = savegame.ProfileId,
            ProfileRevision = savegame.Head?.ProfileRevision,
            // A newly published savegame follows head.
            TargetRevision = null,
            LastObservedHash = slotHash,
            LastPlayedRevision = null
        });
    }

    /// <summary>Writes the name into the slot, now the server holds it. Never while the game runs.</summary>
    /// <returns>Whether the slot now holds exactly what was uploaded.</returns>
    private bool ApplyRename(Game game, ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, IReadOnlyList<GameFileEdit> rename)
    {
        if (rename.Count == 0)
        {
            return true;
        }

        if (runningGuard.IsRunning(game))
        {
            logger.LogWarning("Game {Game} started during the publish, so slot {Slot} keeps its old name.", game.Identity, slot.Value);

            return false;
        }

        return renamer.TryApply(adapter, target, slot, rename);
    }

    /// <summary>Recycles the slot just handed back, unless the game has started meanwhile.</summary>
    private async Task<SavegameLocalCopy> HandBackAsync(Game game, ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string packedSlotHash)
    {
        if (runningGuard.IsRunning(game))
        {
            logger.LogWarning("Game {Game} started during the publish, so slot {Slot} was left where it is.", game.Identity, slot.Value);

            return SavegameLocalCopy.LeftGameRunning;
        }

        return await recycler.HandBackAsync(adapter, target, slot, packedSlotHash);
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
