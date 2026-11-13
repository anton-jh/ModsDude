using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameCheckIn(
    ISavegamesClient savegamesClient,
    ISavegameStore store,
    ISavegamePacker packer,
    ISavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    ISavegameSlots slots,
    ISavegameTransfer transfer,
    ISavegameRecycler recycler,
    ISavegameRenamer renamer,
    ISavegameHolds holds,
    ISavegamePlayAttribution attribution,
    IGameRunningGuard runningGuard,
    TimeProvider time,
    ILogger<SavegameCheckIn> logger)
    : ISavegameCheckIn
{
    /// <remarks>
    /// <para>
    /// Every refusal comes before anything leaves the machine: the game running, a slot with nothing
    /// in it, a save with no known mod list. The rename is packed into the archived copy only, and
    /// reaches the slot once the server has accepted the snapshot it is part of.
    /// </para>
    /// <para>
    /// An unanswered check-in of the same contents is repeated under its id. One of other contents -
    /// the user played on after an answer was lost - is asked about first, so a check-in that did land
    /// is never mistaken for somebody else's.
    /// </para>
    /// </remarks>
    public async Task<SavegameCheckInResult> CheckInAsync(
        Game game,
        Guid savegameId,
        string? label,
        bool keepPlaying,
        bool force,
        bool takeOver,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null,
        string? savegameName = null)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var binding = bindings.RequireBinding(game, savegameId);
        var target = adapter.RequireTarget(game, binding.Slot);
        var slot = binding.Slot.Slot;

        recycler.RecoverSlot(adapter, target, slot);

        if (await slots.ClassifySlotAsync(game, binding.Slot, ct) is SavegameSlotAvailability.Free)
        {
            throw SavegameRequirements.SlotIsEmpty(game, binding.Slot);
        }

        // Asked now, before the upload, so a save that cannot name its mod list costs nothing. An
        // observation can only add a played revision, never take one away, so this answer stands.
        ResolvePlayedRevision(game, binding);

        var rename = renamer.Plan(adapter, target, slot, savegameName);
        var packed = await packer.PackAsync(adapter, target, slot, rename, ct, progress);

        CheckInSavegameResponse response;

        try
        {
            if (packed.EntryCount == 0)
            {
                throw SavegameRequirements.SlotIsEmpty(game, binding.Slot);
            }

            // The slot as it is on disk, which is what play is measured by. The packer hashed it on
            // the way past, so this costs no second pass.
            binding = attribution.Observe(game.Identity, binding, packed.SlotHash);

            var details = await slots.ReadDetailsAsync(adapter, target, slot, ct);

            if (binding.PendingCheckIn is { } unanswered && unanswered.ContentHash != packed.ContentHash)
            {
                binding = await SettleAsync(game, binding, unanswered, ct);
            }

            var pending = binding.PendingCheckIn ?? new SavegamePendingCheckIn(Guid.NewGuid(), packed.ContentHash, packed.SlotHash);

            binding = bindings.Update(game.Identity, savegameId, x => x with { PendingCheckIn = pending })
                ?? throw new InvalidOperationException($"Savegame '{savegameId}' stopped being held in game '{game.Identity}' while it was being checked in.");

            await transfer.UploadAsync(binding.RepoId, savegameId, packed, progress, ct);

            progress?.Report(new SavegameProgress(SavegameStage.Recording, 0, 0));

            var playedRevision = ResolvePlayedRevision(game, binding);

            response = await store.WriteAsync(binding.RepoId, token => savegamesClient.CheckInSavegameV1Async(binding.RepoId, savegameId, new CheckInSavegameRequest
            {
                RequestId = pending.RequestId,
                BasedOn = binding.Snapshot,
                ProfileRevision = playedRevision,
                ContentHash = packed.ContentHash,
                SizeBytes = packed.SizeBytes,
                Label = label,
                Force = force,
                KeepPlaying = keepPlaying,
                TakeOver = takeOver,
                Details = details
            }, token), ct);
        }
        finally
        {
            FileSystemHelper.TryDeleteFile(packed.FilePath, logger);
        }

        var snapshot = response.Snapshot;

        // The server's answer rather than the request's: a repeat is answered as the first one was,
        // whatever it asked for this time.
        if (response.HoldsClaim)
        {
            var slotHash = ApplyRename(game, adapter, target, slot, rename) ? packed.ContentHash : packed.SlotHash;

            // Rebased onto what was just minted, and the attribution starts over.
            bindings.Update(game.Identity, savegameId, x => x with
            {
                Snapshot = snapshot.Number,
                ContentHash = slotHash,
                WrittenAt = time.GetUtcNow().UtcDateTime,
                ProfileId = snapshot.ProfileId,
                ProfileRevision = snapshot.ProfileRevision,
                LastObservedHash = slotHash,
                LastPlayedRevision = null,
                PendingCheckIn = null
            });

            return new SavegameCheckInResult(snapshot, true, SavegameLocalCopy.Kept, response.TakenFrom);
        }

        // The binding goes before the folder, so a failed recycle leaves an unrecognised slot rather than
        // one claimed by a savegame that is no longer checked out.
        bindings.ClearBinding(game.Identity, savegameId);

        return new SavegameCheckInResult(snapshot, false, await HandBackAsync(game, adapter, target, slot, packed.SlotHash), response.TakenFrom);
    }

    public async Task<SavegameLocalCopy> DiscardAsync(Game game, Guid savegameId, CancellationToken ct)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var binding = bindings.RequireBinding(game, savegameId);

        // Before the server call: discarding promises the Recycle Bin, and a hold whose folder the
        // settings no longer name cannot keep that promise.
        var target = adapter.RequireTarget(game, binding.Slot);

        recycler.RecoverSlot(adapter, target, binding.Slot.Slot);

        await holds.ReleaseAsync(game, binding.RepoId, savegameId, ct);

        if (runningGuard.IsRunning(game))
        {
            logger.LogWarning("Savegame {Savegame} was given back while game {Game} started; its slot was left alone.", savegameId, game.Identity);

            return SavegameLocalCopy.LeftGameRunning;
        }

        return await recycler.RecycleSlotAsync(adapter, target, binding.Slot.Slot)
            ? SavegameLocalCopy.Recycled
            : SavegameLocalCopy.LeftBehind;
    }

    /// <summary>
    /// Finds out what became of a check-in whose answer was lost, and brings the binding up to date
    /// with it. Only ever asked once the slot has changed since, so it cannot simply be repeated.
    /// </summary>
    /// <returns>The binding with no pending check-in, rebased where the check-in landed.</returns>
    /// <exception cref="UserFriendlyException">It landed and handed the save back.</exception>
    private async Task<SavegameCheckoutBinding> SettleAsync(
        Game game, SavegameCheckoutBinding binding, SavegamePendingCheckIn unanswered, CancellationToken ct)
    {
        CheckInSavegameResponse answer;

        try
        {
            answer = await savegamesClient.GetSavegameCheckInV1Async(binding.RepoId, binding.SavegameId, unanswered.RequestId, ct);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.SavegameCheckInUnknown)
        {
            logger.LogInformation(
                "Unanswered check-in {Request} of savegame {Savegame} never landed; a new one is sent in its place.",
                unanswered.RequestId, binding.SavegameId);

            return bindings.Update(game.Identity, binding.SavegameId, x => x with { PendingCheckIn = null }) ?? binding;
        }

        if (answer.HoldsClaim is false)
        {
            // It handed the save back, and the slot has been played since. That play is nobody's
            // savegame now, so the slot becomes an ordinary save of the user's own rather than being
            // checked in on top of a claim that has ended.
            bindings.ClearBinding(game.Identity, binding.SavegameId);

            throw new UserFriendlyException(
                "That savegame was already checked in",
                $"Check-in {unanswered.RequestId} of savegame '{binding.SavegameId}' landed as snapshot {answer.Snapshot.Number} and handed the save back, but its answer was lost and the slot was played on afterwards.")
            {
                Hint = "What you played since is still in the slot, as a save of your own."
            };
        }

        logger.LogInformation(
            "Unanswered check-in {Request} of savegame {Savegame} landed as snapshot {Snapshot}; the binding is rebased onto it.",
            unanswered.RequestId, binding.SavegameId, answer.Snapshot.Number);

        // What the slot held when that check-in was packed is what matched the head. The play since is
        // already observed, so the attribution is left as it is.
        return bindings.Update(game.Identity, binding.SavegameId, x => x with
        {
            Snapshot = answer.Snapshot.Number,
            ContentHash = unanswered.SlotHash,
            ProfileId = answer.Snapshot.ProfileId,
            ProfileRevision = answer.Snapshot.ProfileRevision,
            PendingCheckIn = null
        }) ?? binding;
    }

    /// <summary>Writes the rename into the slot, now the server holds it. Never while the game runs.</summary>
    /// <returns>Whether the slot now holds exactly what was uploaded.</returns>
    private bool ApplyRename(Game game, ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, IReadOnlyList<GameFileEdit> rename)
    {
        if (rename.Count == 0)
        {
            return true;
        }

        if (runningGuard.IsRunning(game))
        {
            logger.LogWarning("Game {Game} started during the check-in, so slot {Slot} keeps its old name.", game.Identity, slot.Value);

            return false;
        }

        return renamer.TryApply(adapter, target, slot, rename);
    }

    /// <summary>Recycles the slot just handed back, unless the game has started meanwhile.</summary>
    private async Task<SavegameLocalCopy> HandBackAsync(Game game, ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string packedSlotHash)
    {
        if (runningGuard.IsRunning(game))
        {
            logger.LogWarning("Game {Game} started during the check-in, so slot {Slot} was left where it is.", game.Identity, slot.Value);

            return SavegameLocalCopy.LeftGameRunning;
        }

        return await recycler.HandBackAsync(adapter, target, slot, packedSlotHash);
    }

    /// <summary>Refuses rather than guesses where a savegame on a profile has no known revision.</summary>
    private int? ResolvePlayedRevision(Game game, SavegameCheckoutBinding binding)
    {
        if (binding.ProfileId is null)
        {
            return null;
        }

        return attribution.FindPlayedRevision(game.Identity, binding)
            ?? throw new UserFriendlyException(
                $"'{game.Name}' has no record of which mod list it is on",
                $"Neither the sync manifest for game '{game.Identity}' nor the checkout binding records a profile revision, and a savegame that follows a mod list has to name one. Apply the profile to this game and check in again.")
            {
                Hint = "Apply the profile to this game and check in again."
            };
    }
}
