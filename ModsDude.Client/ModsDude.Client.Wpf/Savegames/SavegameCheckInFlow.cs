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
                $"Checking {savegame.Quoted} in",
                "checking a savegame in",
                $"Check-in of {savegame.Quoted} stopped. Nothing was recorded, and your copy is still in its slot.",
                [savegameId],
                AllFolders: false)
            {
                Detail = "Packing and uploading what is in the slot"
            },
            async (task, ct) =>
            {
                var outcome = await CheckInAsync(game, savegameId, savegame, step.TrimmedLabel, step.KeepPlaying, task, ct);

                if (outcome.WasDeferred)
                {
                    toasts.Show($"Left as it is. Your copy of '{savegameName}' is still in its slot.");

                    return;
                }

                if (outcome.Succeeded is false)
                {
                    return;
                }

                if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, slot, game.Name) is string notRecycled)
                {
                    toasts.Show(
                        $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server, and the save is anybody's to take. {notRecycled}",
                        ToastSeverity.Warning);
                }
                else if (outcome.TakenFrom is SavegameCheckoutDto takenFrom)
                {
                    toasts.Show(
                        $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server. The save is still in '{game.Name}' and yours again - " +
                        $"{takenFrom.User.DisplayName} no longer has it, and their ModsDude will tell them.");
                }
                else
                {
                    toasts.Show(outcome.KeptPlaying
                        ? $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server. The save is still in '{game.Name}' and still yours."
                        : $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server, and the save is anybody's to take.");
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
            ? $"{SavegameSlotWording.Capitalised(slot)} has been played since it was downloaded, and none of that has been checked in. " +
              "It goes to the Recycle Bin and no snapshot is minted, so the only copy of that play is one you restore by hand."
            : $"{SavegameSlotWording.Capitalised(slot)} goes to the Recycle Bin and no snapshot is minted. The savegame goes back to being anybody's to take.";

        var modal = new ConfirmationModalViewModel(
            $"Give '{savegameName}' back without checking it in?",
            consequence,
            hasUnpublishedPlay ? IconKind.Warning : IconKind.Question,
            "Discard it - the local copy goes to the Recycle Bin",
            "Keep it checked out");

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return false;
        }

        return await operations.RunAsync(
            new SavegameOperation(
                game,
                $"Giving '{savegameName}' back",
                "giving a savegame back",
                $"Giving '{savegameName}' back stopped. It is still checked out to you, and your copy is still in its slot.",
                [savegameId],
                AllFolders: false)
            {
                Detail = "Releasing the claim, then recycling the local copy"
            },
            async (_, ct) =>
            {
                var localCopy = await savegameCheckIn.DiscardAsync(game, savegameId, ct);

                toasts.Show(SavegameSlotWording.NotRecycled(localCopy, slot, game.Name) is string notRecycled
                        ? $"'{savegameName}' was given back without a snapshot. {notRecycled}"
                        : $"'{savegameName}' was given back without a snapshot. The local copy is in the Recycle Bin.",
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
            task.Report($"Checking {savegame.Quoted} in");
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
                $"{name} has {savegame.Quoted} checked out",
                $"They have had it since {SavegameWording.Exactly(holder.TakenAt)}. Keeping playing takes it from them, " +
                "and their ModsDude will tell them. Leaving it with them checks nothing in and keeps your copy where it is.",
                IconKind.Warning,
                $"Take it from {name}",
                "Leave it with them");

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
                $"Somebody else checked {savegame.Quoted} in",
                "Your save was built on an older snapshot. Checking yours in anyway records it as the newest one, with " +
                "theirs named beside it and still in the history - nothing is deleted either way. Leaving it alone keeps " +
                "your copy exactly where it is, so you can look at theirs first and decide.",
                IconKind.Warning,
                "Check mine in anyway - theirs stays in the history",
                "Leave mine alone for now");

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
            ? $"Played on revision {revision} of its mod list."
            : $"Played on {profile.Name} rev {revision}.";
    }

    /// <summary>The number the player knows a held savegame's slot by, for a game that numbers them.</summary>
    private int? HeldSlotNumber(Game game, Guid savegameId)
        => bindingStore.GetBinding(game.Identity, savegameId) is SavegameCheckoutBinding binding
            ? savegameSlots.DescribeSlotNumber(game, binding.Slot)
            : null;
}
