using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegameCheckInFlow(
    ISavegameSlots savegameSlots,
    ISavegameCheckIn savegameCheckIn,
    ISavegamePlayAttribution playAttribution,
    ISavegameBindingStore bindingStore,
    IProfileStore profileStore,
    IDriftMonitor driftMonitor,
    ISavegameOperations operations,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IToastService toasts) : ISavegameCheckInFlow
{
    public SavegameCheckInStepViewModel CreateStep(
        Game game,
        Guid savegameId,
        HeldSavegameName savegame,
        string? handBackReason = null)
        => new(savegame, DescribePlayedOn(game, savegameId), HeldSlotNumber(game, savegameId), handBackReason);

    public Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        HeldSavegameName savegame,
        string? label,
        bool keepPlaying,
        IBackgroundTask task,
        CancellationToken cancellationToken)
        => SendAsync(game, savegameId, savegame, label, keepPlaying, force: false, task, cancellationToken);

    public async Task CheckInHeldAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        CancellationToken cancellationToken)
    {
        // The savegame's name stands in for the slot label: the slot id is a folder name the player
        // never thinks in. It is this savegame's own record, so it is also the name the snapshot
        // carries. The slot is named now, while the binding that knows it is still there.
        var slot = SavegameSlotWording.Named(HeldSlotNumber(game, savegameId), savegameName);

        var savegame = new HeldSavegameName(savegameName, null);
        var step = CreateStep(game, savegameId, savegame);

        try
        {
            if (await WizardModalViewModel.Single(step, cancellationToken).ShowAsync(modalService.Value) is false)
            {
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page asking went away before anything was answered. Nothing has started.
            return;
        }

        await operations.RunAsync(
            new SavegameOperation(
                game,
                $"Checking in {savegame.Quoted}",
                "checking a savegame in",
                $"Check-in of {savegame.Quoted} cancelled",
                [savegameId],
                AllFolders: false)
            {
                Detail = "Packing and uploading the slot"
            },
            async (task, ct) =>
            {
                var outcome = await CheckInAsync(game, savegameId, savegame, step.TrimmedLabel, step.KeepPlaying, task, ct);

                if (outcome.WasDeferred)
                {
                    toasts.Show($"'{savegameName}' was not checked in");

                    return;
                }

                if (outcome.Succeeded is false)
                {
                    return;
                }

                if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, slot, game.Name) is string notRecycled)
                {
                    toasts.Show(
                        $"Checked in '{savegameName}' as snapshot {outcome.Snapshot!.Number}. {notRecycled}",
                        ToastSeverity.Warning);
                }
                else if (outcome.TakenFrom is SavegameCheckoutDto takenFrom)
                {
                    toasts.Show(
                        $"Checked in '{savegameName}' as snapshot {outcome.Snapshot!.Number}. It is checked out to you now, not to {takenFrom.User.DisplayName}.");
                }
                else
                {
                    toasts.Show(outcome.KeptPlaying
                        ? $"Checked in '{savegameName}' as snapshot {outcome.Snapshot!.Number}. It is still checked out to you."
                        : $"Checked in '{savegameName}' as snapshot {outcome.Snapshot!.Number}");
                }

                await driftMonitor.CheckAsync();
            });
    }

    public async Task<bool> DiscardAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        CancellationToken cancellationToken)
    {
        if (bindingStore.GetBinding(game.Identity, savegameId) is not SavegameCheckoutBinding binding)
        {
            return false;
        }

        var slot = SavegameSlotWording.Named(HeldSlotNumber(game, savegameId), savegameName);

        bool hasUnpublishedPlay;

        try
        {
            // Asked of the disk rather than read off a row's chip, which arrives from a background pass
            // that may not have got there yet - and the two confirmations this decides between are
            // "nothing is lost" and "an evening of play goes to the Recycle Bin".
            hasUnpublishedPlay = await savegameSlots.ClassifySlotAsync(game, binding.Slot, cancellationToken)
                is SavegameSlotAvailability.HeldWithUnpublishedPlay;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "giving a savegame back");

            return false;
        }

        var consequence = hasUnpublishedPlay
            ? $"{SavegameSlotWording.Capitalised(slot)} has progress that is not checked in. It goes to the Recycle Bin, and the progress is not saved anywhere else."
            : $"{SavegameSlotWording.Capitalised(slot)} goes to the Recycle Bin. No snapshot is made.";

        var modal = new ConfirmationModalViewModel(
            $"Discard '{savegameName}'?",
            consequence,
            hasUnpublishedPlay ? IconKind.Warning : IconKind.Question,
            "Discard",
            "Cancel");

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return false;
        }

        return await operations.RunAsync(
            new SavegameOperation(
                game,
                $"Discarding '{savegameName}'",
                "discarding a savegame",
                $"Discarding '{savegameName}' cancelled. It is still checked out to you.",
                [savegameId],
                AllFolders: false)
            {
                Detail = "Ending the check-out, then moving the local copy to the Recycle Bin"
            },
            async (_, ct) =>
            {
                var localCopy = await savegameCheckIn.DiscardAsync(game, savegameId, ct);

                toasts.Show(SavegameSlotWording.NotRecycled(localCopy, slot, game.Name) is string notRecycled
                        ? $"Discarded '{savegameName}'. {notRecycled}"
                        : $"Discarded '{savegameName}'. The local copy is in the Recycle Bin.",
                    localCopy is SavegameLocalCopy.Recycled ? ToastSeverity.Info : ToastSeverity.Warning);

                await driftMonitor.CheckAsync();
            });
    }

    /// <summary>
    /// Sends the check-in, and turns the two refusals that need a decision into questions: a stale base,
    /// and somebody else holding the claim of a save being kept.
    /// </summary>
    /// <remarks>
    /// A forced check-in becomes the head with the snapshot it was built on recorded beside it, so
    /// neither answer to a stale base loses anything.
    /// </remarks>
    private async Task<SavegameCheckInOutcome> SendAsync(
        Game game,
        Guid savegameId,
        HeldSavegameName savegame,
        string? label,
        bool keepPlaying,
        bool force,
        IBackgroundTask task,
        CancellationToken cancellationToken,
        bool takeOver = false)
    {
        try
        {
            task.Report($"Checking in {savegame.Quoted}");
            task.DeclareTransfers(TransferDirection.Upload);

            var result = await savegameCheckIn.CheckInAsync(
                game, savegameId, label, keepPlaying, force, takeOver, cancellationToken, new SavegameStripProgress(task), savegame.Name);

            return SavegameCheckInOutcome.CheckedIn(result);
        }
        catch (ApiException<CustomProblemDetails> exception)
            when (exception.Result.Type is ProblemType.SavegameClaimHeldByOther && exception.Result.Holder is SavegameCheckoutDto holder)
        {
            var name = holder.User.DisplayName;

            var choice = new ConfirmationModalViewModel(
                $"{savegame.Subject} is checked out to {name}",
                $"Checked out since {SavegameWording.Exactly(holder.TakenAt)}. {name} is told if you take it.",
                IconKind.Warning,
                $"Take it from {name}",
                "Cancel");

            await modalService.Value.Show(choice);

            if (choice.Result is false)
            {
                return SavegameCheckInOutcome.Deferred;
            }

            return await SendAsync(game, savegameId, savegame, label, keepPlaying, force, task, cancellationToken, takeOver: true);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.SavegameSnapshotStale)
        {
            var choice = new ConfirmationModalViewModel(
                $"Somebody else checked in {savegame.Quoted}",
                "Your copy is based on an older snapshot. Checking in anyway makes yours the latest snapshot. Theirs stays in the history.",
                IconKind.Warning,
                "Check in anyway",
                "Cancel");

            await modalService.Value.Show(choice);

            if (choice.Result is false)
            {
                return SavegameCheckInOutcome.Deferred;
            }

            return await SendAsync(game, savegameId, savegame, label, keepPlaying, force: true, task, cancellationToken, takeOver);
        }
        catch (UserFriendlyException exception)
        {
            await errorReporter.ShowAsync(exception, "checking a savegame in");

            return SavegameCheckInOutcome.Cancelled;
        }
    }

    /// <summary>
    /// Which mod list the snapshot about to be minted records. The revision is
    /// <see cref="ISavegamePlayAttribution.GetPlayedRevision"/>'s, the same one the check-in sends.
    /// </summary>
    private string? DescribePlayedOn(Game game, Guid savegameId)
    {
        if (bindingStore.GetBinding(game.Identity, savegameId) is not SavegameCheckoutBinding binding
            || binding.ProfileId is not Guid profileId
            || playAttribution.GetPlayedRevision(game, savegameId) is not int revision)
        {
            return null;
        }

        var profile = profileStore.Find(binding.RepoId, profileId);

        return profile is null
            ? $"Played on revision {revision}"
            : $"Played on {profile.Name} revision {revision}";
    }

    /// <summary>The number the player knows a held savegame's slot by, for a game that numbers them.</summary>
    private int? HeldSlotNumber(Game game, Guid savegameId)
        => bindingStore.GetBinding(game.Identity, savegameId) is SavegameCheckoutBinding binding
            ? savegameSlots.DescribeSlotNumber(game, binding.Slot)
            : null;
}
