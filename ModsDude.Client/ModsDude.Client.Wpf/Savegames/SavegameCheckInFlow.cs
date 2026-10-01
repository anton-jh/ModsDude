using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
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
    IProfileService profileService,
    IDriftMonitor driftMonitor,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts) : ISavegameCheckInFlow
{
    public async Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        CancellationToken cancellationToken,
        string? renameTo = null)
    {
        var modal = new SavegameCheckInModalViewModel(
            savegameName, slotLabel, DescribePlayedOn(game, savegameId), HeldSlotNumber(game, savegameId));

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return SavegameCheckInOutcome.Cancelled;
        }

        return await SendAsync(game, savegameId, savegameName, modal.TrimmedLabel, modal.KeepPlaying, force: false, cancellationToken, renameTo);
    }

    public async Task CheckInHeldAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        Func<Task> changed,
        CancellationToken cancellationToken)
    {
        try
        {
            // The savegame's name stands in for the slot label: the slot id is a folder name the player
            // never thinks in. It is this savegame's own record, so it is also the name to write into
            // the slot. The slot is named now, while the binding that knows it is still there.
            var slot = SavegameSlotWording.Named(HeldSlotNumber(game, savegameId), savegameName);

            var outcome = await CheckInAsync(game, savegameId, savegameName, savegameName, cancellationToken, renameTo: savegameName);

            if (outcome.WasDeferred)
            {
                toasts.Show($"Left as it is. Your copy of '{savegameName}' is still in its slot and still yours.");

                return;
            }

            if (outcome.Succeeded is false)
            {
                return;
            }

            if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, slot) is string notRecycled)
            {
                toasts.Show(
                    $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server, and the save is anybody's to take. {notRecycled}",
                    ToastSeverity.Warning);
            }
            else
            {
                toasts.Show(outcome.KeptPlaying
                    ? $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server. The save is still in '{game.Name}' and still yours."
                    : $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server, and the save is anybody's to take.");
            }

            await driftMonitor.CheckAsync();
            await changed();
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-upload. The next read of the page says whether it landed.
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "checking a savegame in");
        }
    }

    public async Task<bool> DiscardAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        bool hasUnpublishedPlay,
        CancellationToken cancellationToken)
    {
        var slot = SavegameSlotWording.Named(HeldSlotNumber(game, savegameId), slotLabel);
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

        using var task = backgroundTasks.Begin($"Giving '{savegameName}' back", "Releasing the claim, then recycling the local copy");

        var recycled = await savegameCheckIn.DiscardAsync(game, savegameId, cancellationToken);

        toasts.Show(recycled
            ? $"'{savegameName}' was given back without a snapshot. The local copy is in the Recycle Bin."
            : $"'{savegameName}' was given back without a snapshot. {SavegameSlotWording.LeftBehind(slot)}",
            recycled ? ToastSeverity.Info : ToastSeverity.Warning);

        return true;
    }

    /// <summary>
    /// A forced check-in becomes the head with the snapshot it was built on recorded beside it, so
    /// neither answer to a stale base loses anything.
    /// </summary>
    private async Task<SavegameCheckInOutcome> SendAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string? label,
        bool keepPlaying,
        bool force,
        CancellationToken cancellationToken,
        string? renameTo = null)
    {
        try
        {
            using var task = backgroundTasks.Begin($"Checking '{savegameName}' in", "Packing and uploading what is in the slot");
            task.DeclareTransfers(TransferDirection.Upload);

            var result = await savegameCheckIn.CheckInAsync(
                game, savegameId, label, keepPlaying, force, cancellationToken, new SavegameStripProgress(task), renameTo);

            return SavegameCheckInOutcome.CheckedIn(result.Snapshot, keepPlaying, result.LocalCopy);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.SavegameSnapshotStale)
        {
            var choice = new ConfirmationModalViewModel(
                $"Somebody else checked '{savegameName}' in",
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

            return await SendAsync(game, savegameId, savegameName, label, keepPlaying, force: true, cancellationToken, renameTo);
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

        var profile = profileService.Profiles.FirstOrDefault(x => x.Id == profileId);

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
