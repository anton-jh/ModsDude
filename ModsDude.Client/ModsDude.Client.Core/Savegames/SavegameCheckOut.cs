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
    IGameRunningGuard runningGuard,
    TimeProvider time)
    : ISavegameCheckOut
{
    /// <summary>One page is every snapshot a savegame will have; retention keeps ten.</summary>
    private const int _snapshotPageSize = 200;


    /// <remarks>
    /// <para>
    /// The order is the safety property: the local refusals first, so a refusal costs nothing; then the
    /// claim; then the slow download; and the binding last, once the bytes are in place.
    /// </para>
    /// <para>
    /// A failure after the claim leaves the claim taken and nothing bound. Releasing it would let
    /// somebody else take a save whose bytes may already be on this disk. Retrying is safe: taking a
    /// claim you already hold is not a conflict.
    /// </para>
    /// </remarks>
    public async Task<SavegameCheckOutResult> CheckOutAsync(Game game, SavegameDto savegame, SavegameSlotRef slot, SavegameRevisionMode revisionMode, CancellationToken ct, IProgress<SavegameProgress>? progress = null)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var target = adapter.RequireTarget(game, slot);
        var head = savegame.Head
            ?? throw new UserFriendlyException(
                $"'{savegame.Name}' has nothing to check out",
                $"Savegame '{savegame.Id}' has no head snapshot, so there is nothing to write into a slot.");

        // The snapshot's profile rather than the savegame's, because that is what the binding records.
        bindings.EnsureModFolderIsFree(game, savegame.Id, head.ProfileId, savegame.Name);

        await EnsureWritableAsync(game, slot, savegame.Name, ct);

        var claim = await TakeClaimAsync(savegame, head, ct);

        store.RecordOwnClaim(savegame.RepoId, savegame.Id, claim.Checkout);

        // The head the claim was granted on, which the request made sure is the one the caller saw.
        head = claim.Head;

        var displaced = await transfer.DownloadIntoSlotAsync(adapter, target, savegame.RepoId, savegame.Id, head.ContentHash, slot.Slot, progress, ct);

        bindings.SetBinding(game.Identity, new SavegameCheckoutBinding(
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

        var holder = claim.TakenFrom is SavegameCheckoutDto takenFrom
            ? new SavegameClaimHolder(takenFrom.User.Id, takenFrom.User.DisplayName, takenFrom.TakenAt)
            : null;

        return new SavegameCheckOutResult(holder, displaced);
    }

    /// <summary>
    /// Takes the claim on the terms the caller saw: this head, and this holder or none. Anything else is
    /// refused by the server - the store reads the savegame again on a refusal - and the caller is told
    /// to look again.
    /// </summary>
    private async Task<CheckOutSavegameResponse> TakeClaimAsync(SavegameDto savegame, SavegameSnapshotDto head, CancellationToken ct)
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
                ct);
        }
        catch (ApiException<CustomProblemDetails> exception)
            when (exception.Result.Type is ProblemType.SavegameHeadMoved or ProblemType.SavegameClaimChanged)
        {
            throw new UserFriendlyException(
                $"'{savegame.Name}' changed",
                "Somebody checked it in or took it just now. Look at it again before checking it out.",
                exception);
        }
    }

    public async Task<DisplacedSavegame?> TakeCopyAsync(Game game, SavegameDto savegame, int snapshotNumber, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var target = adapter.RequireTarget(game, slot);

        await EnsureWritableAsync(game, slot, savegame.Name, ct);

        var contentHash = await ResolveSnapshotHashAsync(savegame, snapshotNumber, ct);

        var displaced = await transfer.DownloadIntoSlotAsync(adapter, target, savegame.RepoId, savegame.Id, contentHash, slot.Slot, progress, ct);

        // A binding left on this slot would name contents that are now a different savegame. Its claim
        // stays open on the server for the caller to offer to give back.
        if (bindings.GetBindingForSlot(game.Identity, slot) is SavegameCheckoutBinding displacedBinding)
        {
            bindings.ClearBinding(game.Identity, displacedBinding.SavegameId);
        }

        return displaced;
    }

    /// <summary>
    /// Refuses to write into a slot holding play nobody has checked in. A slot that only needs a
    /// confirmation has had it by now, in the picker.
    /// </summary>
    private async Task EnsureWritableAsync(Game game, SavegameSlotRef slot, string savegameName, CancellationToken ct)
    {
        var availability = await slots.ClassifySlotAsync(game, slot, ct);

        if (SavegameSlotStates.IsRefused(availability) is false)
        {
            return;
        }

        throw new UserFriendlyException(
            "That slot holds play nobody has checked in",
            $"Writing '{savegameName}' into slot '{slot}' would destroy a savegame that has been played since it was checked out and exists nowhere else. Check that one in first.");
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
}
