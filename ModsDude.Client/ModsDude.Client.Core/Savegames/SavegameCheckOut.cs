using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameCheckOut(
    ISavegamesClient savegamesClient,
    ISavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    ISavegameSlots slots,
    ISavegameTransfer transfer,
    ISavegameStore store,
    ISavegameRecycler recycler,
    ISavegameHolds holds,
    IGameRunningGuard runningGuard,
    TimeProvider time)
    : ISavegameCheckOut
{
    /// <summary>One page is every snapshot a savegame will have; retention keeps ten.</summary>
    private const int _snapshotPageSize = 200;


    /// <remarks>
    /// <para>
    /// The order is the safety property: the local refusals first, so a refusal costs nothing; then the
    /// restore and the claim, together and past the point of cancelling, so neither is ever left
    /// without the other; then the slow download; and then everything is looked at again - the game,
    /// the slot, the copy being moved - right before the slot changes and the binding is written.
    /// </para>
    /// <para>
    /// A failure or a cancel after the claim leaves the claim taken and nothing bound. Releasing it
    /// would let somebody else take a save whose bytes may already be on this disk. Retrying is safe:
    /// taking a claim you already hold is not a conflict.
    /// </para>
    /// </remarks>
    public async Task<SavegameCheckOutResult> CheckOutAsync(
        Game game,
        SavegameDto savegame,
        int snapshotNumber,
        Guid restoreRequestId,
        SavegameSlotRef slot,
        SavegameRevisionMode revisionMode,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var target = adapter.RequireTarget(game, slot);
        var head = savegame.Head ?? throw NothingToWrite(savegame);

        // The snapshot's profile rather than the savegame's, because that is what the binding records.
        bindings.EnsureModFolderIsFree(game, savegame.Id, head.ProfileId, savegame.Name);

        recycler.RecoverSlot(adapter, target, slot.Slot);

        await PlanWriteAsync(game, slot, savegame.Id, savegame.Name, ct);

        ct.ThrowIfCancellationRequested();

        // Restoring copies an older snapshot forward, so the claim that follows has no stale base.
        if (snapshotNumber != head.Number)
        {
            savegame = await RestoreAsync(savegame, restoreRequestId, snapshotNumber);
            head = savegame.Head!;
        }

        var claim = await TakeClaimAsync(savegame, head);

        store.RecordOwnClaim(savegame.RepoId, savegame.Id, claim.Checkout);

        // The head the claim was granted on, which the request made sure is the one the caller saw.
        head = claim.Head;
        savegame = savegame with { Head = head };

        using var downloaded = await transfer.DownloadAsync(savegame.RepoId, savegame.Id, head.ContentHash, progress, ct);

        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var write = await PlanWriteAsync(game, slot, savegame.Id, savegame.Name, ct);

        await ReleaseDisplacedHoldAsync(game, write);

        var displaced = await transfer.WriteIntoSlotAsync(downloaded, adapter, target, slot.Slot, progress, ct);

        // Straight after the bytes land, with nothing in between that can stop it: a slot written and
        // not bound is play nothing can name.
        bindings.Bind(game.Identity, new SavegameCheckoutBinding(
            savegame.RepoId,
            savegame.Id,
            slot,
            head.Number,
            head.ContentHash,
            time.GetUtcNow().UtcDateTime)
        {
            ProfileId = head.ProfileId,
            ProfileRevision = head.ProfileRevision,
            TargetRevision = SavegameRevisionRules.PinnedRevision(revisionMode, head.ProfileRevision),
            // The bytes just written are the first observation, and nothing has been played on them.
            LastObservedHash = head.ContentHash,
            LastPlayedRevision = null
        });

        var previousCopy = write.MovesFrom is SavegameCheckoutBinding moved
            ? await HandBackPreviousCopyAsync(game, moved)
            : (SavegameLocalCopy?)null;

        var holder = claim.TakenFrom is SavegameCheckoutDto takenFrom
            ? new SavegameClaimHolder(takenFrom.User.Id, takenFrom.User.DisplayName, takenFrom.TakenAt)
            : null;

        return new SavegameCheckOutResult(savegame, holder, displaced, previousCopy);
    }

    public async Task<DisplacedSavegame?> TakeCopyAsync(Game game, SavegameDto savegame, int snapshotNumber, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var target = adapter.RequireTarget(game, slot);

        recycler.RecoverSlot(adapter, target, slot.Slot);

        await PlanWriteAsync(game, slot, null, savegame.Name, ct);

        var contentHash = await ResolveSnapshotHashAsync(savegame, snapshotNumber, ct);

        using var downloaded = await transfer.DownloadAsync(savegame.RepoId, savegame.Id, contentHash, progress, ct);

        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var write = await PlanWriteAsync(game, slot, null, savegame.Name, ct);

        await ReleaseDisplacedHoldAsync(game, write);

        return await transfer.WriteIntoSlotAsync(downloaded, adapter, target, slot.Slot, progress, ct);
    }

    /// <summary>
    /// What writing into the slot has to do besides writing, refusing first wherever it would destroy
    /// play nobody has checked in: in the slot itself, or in the copy of this savegame being moved
    /// out of another slot. A slot that only needs a confirmation has had it by now, in the picker.
    /// </summary>
    /// <param name="moving">The savegame being checked out, whose own copy elsewhere moves here. Null for a copy.</param>
    private async Task<SlotWrite> PlanWriteAsync(Game game, SavegameSlotRef slot, Guid? moving, string savegameName, CancellationToken ct)
    {
        if (SavegameSlotStates.IsRefused(await slots.ClassifySlotAsync(game, slot, ct)))
        {
            throw new UserFriendlyException(
                "That slot holds play nobody has checked in",
                $"Writing '{savegameName}' into slot '{slot}' would destroy a savegame that has been played since it was checked out and exists nowhere else. Check that one in first.")
            {
                Hint = "Check that one in first."
            };
        }

        var occupying = bindings.GetBindingForSlot(game.Identity, slot);
        var releases = occupying is SavegameCheckoutBinding other && other.SavegameId != moving ? other : (SavegameCheckoutBinding?)null;

        if (moving is not Guid savegameId
            || bindings.GetBinding(game.Identity, savegameId) is not SavegameCheckoutBinding own
            || own.Slot.Addresses(slot))
        {
            return new SlotWrite(releases, null);
        }

        // A copy in a folder the settings no longer name cannot be looked at, let alone recycled, so
        // it cannot be moved out of either: refused, with the sentence that says how to get it back.
        adapters.Require(game).RequireTarget(game, own.Slot);

        if (SavegameSlotStates.IsRefused(await slots.ClassifySlotAsync(game, own.Slot, ct)))
        {
            throw new UserFriendlyException(
                $"Your copy of '{savegameName}' has play nobody has checked in",
                $"Savegame '{savegameId}' is held in slot '{own.Slot}' and has been played there since it was checked out. Checking it out into slot '{slot}' would leave that play behind. Check it in first.")
            {
                Hint = "Check it in first."
            };
        }

        return new SlotWrite(releases, own);
    }

    /// <summary>
    /// Gives back the savegame whose clean copy the write replaces, so its claim is not left open with
    /// nothing on this machine naming it. Its bytes are on the server, which is why the picker only
    /// asked for a confirmation.
    /// </summary>
    private async Task ReleaseDisplacedHoldAsync(Game game, SlotWrite write)
    {
        if (write.Releases is SavegameCheckoutBinding displaced)
        {
            await holds.ReleaseAsync(game, displaced.RepoId, displaced.SavegameId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Recycles the slot a savegame was moved out of. Its binding has already moved, so this is the
    /// same hand-back a check-in does: only while it still holds exactly what it held when clean.
    /// </summary>
    private async Task<SavegameLocalCopy> HandBackPreviousCopyAsync(Game game, SavegameCheckoutBinding moved)
    {
        if (runningGuard.IsRunning(game))
        {
            return SavegameLocalCopy.LeftGameRunning;
        }

        var adapter = adapters.Require(game);

        return await recycler.HandBackAsync(adapter, adapter.RequireTarget(game, moved.Slot), moved.Slot.Slot, moved.ContentHash);
    }

    /// <summary>
    /// Takes the claim on the terms the caller saw: this head, and this holder or none. Anything else is
    /// refused by the server - the store reads the savegame again on a refusal - and the caller is told
    /// to look again.
    /// </summary>
    /// <remarks>Not cancellable, for the reason the check-out's order gives.</remarks>
    private async Task<CheckOutSavegameResponse> TakeClaimAsync(SavegameDto savegame, SavegameSnapshotDto head)
    {
        var request = new CheckOutSavegameRequest
        {
            ExpectedHead = head.Number,
            ExpectedCheckoutId = savegame.Checkout is { Status: not SavegameCheckoutStatus.Ended } open ? open.Id : null
        };

        try
        {
            return await store.WriteAsync(
                savegame.RepoId,
                token => savegamesClient.CheckOutSavegameV1Async(savegame.RepoId, savegame.Id, request, token),
                CancellationToken.None);
        }
        catch (ApiException<CustomProblemDetails> exception)
            when (exception.Result.Type is ProblemType.SavegameHeadMoved or ProblemType.SavegameClaimChanged)
        {
            throw Changed(savegame, "Somebody checked it in or took it just now.", exception);
        }
    }

    /// <remarks>Not cancellable, for the reason the check-out's order gives.</remarks>
    private async Task<SavegameDto> RestoreAsync(SavegameDto savegame, Guid requestId, int snapshotNumber)
    {
        var request = new RestoreSavegameSnapshotRequest { RequestId = requestId, BasedOn = savegame.Head!.Number };

        try
        {
            var restored = await store.WriteAsync(
                savegame.RepoId,
                token => savegamesClient.RestoreSavegameSnapshotV1Async(savegame.RepoId, savegame.Id, snapshotNumber, request, token),
                CancellationToken.None);

            return savegame with { Head = restored };
        }
        catch (ApiException<CustomProblemDetails> exception)
            when (exception.Result.Type is ProblemType.SavegameSnapshotStale)
        {
            throw Changed(savegame, "Somebody checked it in or restored a snapshot just now.", exception);
        }
    }

    private async Task<string> ResolveSnapshotHashAsync(SavegameDto savegame, int snapshotNumber, CancellationToken ct)
    {
        if (savegame.Head is SavegameSnapshotDto head && head.Number == snapshotNumber)
        {
            return head.ContentHash;
        }

        var snapshots = await savegamesClient.GetSavegameSnapshotsV1Async(savegame.RepoId, savegame.Id, null, _snapshotPageSize, ct);

        return snapshots.Snapshots.FirstOrDefault(x => x.Number == snapshotNumber)?.ContentHash
            ?? throw new UserFriendlyException(
                $"Snapshot {snapshotNumber} of '{savegame.Name}' is not there any more",
                $"Savegame '{savegame.Id}' has no snapshot {snapshotNumber}. Retention keeps the last few snapshots and anything labelled; pruning leaves the gap where an old one was.");
    }

    private static UserFriendlyException NothingToWrite(SavegameDto savegame)
        => new(
            $"'{savegame.Name}' has nothing to check out",
            $"Savegame '{savegame.Id}' has no head snapshot, so there is nothing to write into a slot.");

    private static UserFriendlyException Changed(SavegameDto savegame, string what, Exception inner)
        => new($"'{savegame.Name}' changed", what, inner)
        {
            Hint = "Look at it again before checking it out."
        };


    /// <param name="Releases">Another savegame's clean copy the write replaces, whose claim is given back.</param>
    /// <param name="MovesFrom">This savegame's own copy in another slot, recycled once the new one is bound.</param>
    private readonly record struct SlotWrite(SavegameCheckoutBinding? Releases, SavegameCheckoutBinding? MovesFrom);
}
