using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
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

        // A rename edits the slot's bytes, which an observation cannot tell apart from play. So where a
        // rename is asked for and there is a profile to attribute to, play is observed before it.
        var observeBeforeRename = savegameName is not null && binding.ProfileId is not null;

        if (observeBeforeRename)
        {
            var beforeRename = await packer.HashSlotAsync(adapter, target, slot, ct);

            binding = attribution.Observe(game.Identity, binding, beforeRename);
        }

        if (savegameName is not null)
        {
            renamer.TryRename(adapter, target, slot, savegameName);
        }

        var packed = await packer.PackAsync(adapter, target, slot, ct, progress);

        // The packer hashes what it writes, so this observation costs no second pass. After a rename the
        // difference is the rename alone, so the hash is taken without attributing anything.
        binding = observeBeforeRename
            ? binding with { LastObservedHash = packed.ContentHash }
            : attribution.Observe(game.Identity, binding, packed.ContentHash);

        // Read before the upload, so the details describe the snapshot being minted, rename included.
        var details = await slots.ReadDetailsAsync(adapter, target, slot, ct);

        // The same bytes as an unanswered check-in are the same check-in, so it is repeated under its id
        // and the server answers it as it did the first time.
        var pending = binding.PendingCheckIn is { } unanswered && unanswered.ContentHash == packed.ContentHash
            ? unanswered
            : new SavegamePendingCheckIn(Guid.NewGuid(), packed.ContentHash);

        binding = binding with { PendingCheckIn = pending };
        bindings.SetBinding(game.Identity, binding);

        CheckInSavegameResponse response;

        try
        {
            await transfer.UploadAsync(binding.RepoId, savegameId, packed, progress, ct);

            progress?.Report(new SavegameProgress(SavegameStage.Recording, 0, 0));

            response = await store.WriteAsync(binding.RepoId, token => savegamesClient.CheckInSavegameV1Async(binding.RepoId, savegameId, new CheckInSavegameRequest
            {
                RequestId = pending.RequestId,
                BasedOn = binding.Snapshot,
                ProfileRevision = ResolvePlayedRevision(game, binding),
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
            // Rebased onto what was just minted, which is these bytes, and the attribution starts over.
            bindings.SetBinding(game.Identity, binding with
            {
                Snapshot = snapshot.Number,
                ContentHash = snapshot.ContentHash,
                WrittenAt = time.GetUtcNow().UtcDateTime,
                ProfileId = snapshot.ProfileId,
                ProfileRevision = snapshot.ProfileRevision,
                LastObservedHash = snapshot.ContentHash,
                LastPlayedRevision = null,
                PendingCheckIn = null
            });

            return new SavegameCheckInResult(snapshot, true, SavegameLocalCopy.Kept, response.TakenFrom);
        }

        // The binding goes before the folder, so a failed recycle leaves an unrecognised slot rather than
        // one claimed by a savegame that is no longer checked out.
        bindings.ClearBinding(game.Identity, savegameId);

        return new SavegameCheckInResult(snapshot, false, await recycler.HandBackAsync(adapter, target, slot, packed.ContentHash), response.TakenFrom);
    }

    public async Task<bool> DiscardAsync(Game game, Guid savegameId, CancellationToken ct)
    {
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        var adapter = adapters.Require(game);
        var binding = bindings.RequireBinding(game, savegameId);

        // Before the server call: discarding promises the Recycle Bin, and a hold whose folder the
        // settings no longer name cannot keep that promise.
        var target = adapter.RequireTarget(game, binding.Slot);

        await holds.ReleaseAsync(game, binding.RepoId, savegameId, ct);

        return await recycler.RecycleSlotAsync(adapter, target, binding.Slot.Slot);
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
                $"Neither the sync manifest for game '{game.Identity}' nor the checkout binding records a profile revision, and a savegame that follows a mod list has to name one. Apply the profile to this game and check in again.");
    }
}
