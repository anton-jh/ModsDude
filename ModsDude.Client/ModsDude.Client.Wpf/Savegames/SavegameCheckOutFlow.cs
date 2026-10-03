using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
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
public sealed class SavegameCheckOutFlow(
    ISavegameCheckOut savegameCheckOut,
    ISavegamesClient savegamesClient,
    ISavegameCheckInFlow checkInFlow,
    ISavegameProfileActivation profileActivation,
    ISavegameCheckOutContextBuilder contextBuilder,
    ISavegameBindingStore bindings,
    ISavegameCompatibilityCheck compatibilityCheck,
    IProfileService profileService,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts) : ISavegameCheckOutFlow
{
    public async Task CheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? revisionMode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        CancellationToken cancellationToken)
    {
        try
        {
            await StartAsync(
                repo, savegame, snapshotNumber, playedRevision, mode, revisionMode,
                currentUserId, nameOf, changed, agreedToTakeFrom: null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Navigated away. Nothing was written, and there is no page left to say so on.
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "checking a savegame out");
        }
    }

    /// <param name="agreedToTakeFrom">
    /// Whose claim the user has already agreed to take, so coming back here after checking in a
    /// blocking savegame does not ask twice.
    /// </param>
    private async Task StartAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? revisionMode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        if (savegame.Head is null || snapshotNumber <= 0)
        {
            await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                $"'{savegame.Name}' has no snapshot yet",
                "Nothing has been checked in for this savegame, so there is nothing to write into a slot."));

            return;
        }

        if (repo.Games.FirstOrDefault() is not Game game)
        {
            await modalService.Value.Show(SavegameRefusals.NotConnected(
                repo, "A savegame has to be written into an installation of the game."));

            return;
        }

        // Taking a save from somebody is decided first, because it decides whether there is a
        // check-out at all. A copy takes nothing, and a claim of your own is not somebody else's.
        var holder = savegame.Checkout is SavegameCheckoutDto checkout && checkout.Status is not SavegameCheckoutStatus.Ended
            ? checkout
            : null;
        var heldByMe = holder is not null && currentUserId is not null && holder.User.Id == currentUserId;
        var takingFrom = mode is SavegameCheckOutMode.CheckOut && heldByMe is false ? holder : null;

        if (takingFrom is not null && takingFrom.User.Id != agreedToTakeFrom)
        {
            var confirmation = ConfirmTakeOver(savegame.Name, takingFrom);

            await modalService.Value.Show(confirmation);

            if (confirmation.Result is false)
            {
                return;
            }
        }

        // Decided before the folder is touched, because it decides which revision the folder goes onto.
        // A save already held here keeps the mode it is held in: its own pin refuses any other revision,
        // so switching is checking it in and out again.
        var verdict = await AssessAsync(repo, savegame, playedRevision, cancellationToken);

        var chosenMode = HeldMode(game, savegame)
            ?? revisionMode
            ?? await ChooseRevisionModeAsync(savegame, playedRevision, verdict);

        if (chosenMode is not SavegameRevisionMode chosen)
        {
            return;
        }

        var pinned = SavegameRevisionRules.PinnedRevision(chosen, playedRevision);

        // Before the slot modal, so its mod summary describes the folder as it will be.
        if (await profileActivation.ConfirmActivateFirstAsync(repo, game, savegame, mode, pinned, changed, cancellationToken) is false)
        {
            return;
        }

        var context = await contextBuilder.BuildAsync(repo, savegame, game, mode, pinned, verdict, nameOf, cancellationToken);

        var modal = new SavegameCheckOutModalViewModel(
            mode,
            savegame.Name,
            SavegameWording.ProfileOf(savegame),
            snapshotNumber,
            savegame.Head.Number,
            context);

        await modalService.Value.Show(modal);

        if (modal.CheckInFirstSavegameId is Guid blocking)
        {
            await CheckInBlockingAsync(
                repo, game, blocking, savegame, snapshotNumber, playedRevision, mode, chosen, currentUserId, nameOf, changed,
                takingFrom?.User.Id, cancellationToken);

            return;
        }

        if (modal.Result is not SavegameSlotOptionViewModel slot)
        {
            return;
        }

        await ExecuteAsync(repo, savegame, snapshotNumber, mode, chosen, game, slot, changed, takingFrom?.User.Id, cancellationToken);
    }

    /// <summary>
    /// The question asked before taking a save somebody else has checked out. Taking it is always
    /// allowed, but leaves two copies of one save, and whoever checks in second overwrites the other.
    /// </summary>
    private static ConfirmationModalViewModel ConfirmTakeOver(string savegameName, SavegameCheckoutDto holder)
    {
        var name = holder.User.DisplayName;

        return new ConfirmationModalViewModel(
            $"{name} has '{savegameName}' checked out",
            $"They have had it since {SavegameWording.Exactly(holder.TakenAt)}. Checking it out takes it from them, "
                + "and their ModsDude will tell them.\n\n"
                + "If they are playing it, you will each have a copy of the same save: whoever checks in second has "
                + "to force it, and that overwrites the other's play.",
            IconKind.Warning,
            $"Take it from {name}",
            "Leave it with them");
    }

    private SavegameRevisionMode? HeldMode(Game game, SavegameDto savegame)
        => bindings.GetBinding(game.Identity, savegame.Id) is SavegameCheckoutBinding binding
            ? binding.TargetRevision is null ? SavegameRevisionMode.Latest : SavegameRevisionMode.Compatibility
            : null;

    /// <summary>
    /// How far the latest mod list has moved from the one the snapshot was played on. Null where there
    /// is nothing to compare: no mod list, a profile this member cannot see, or nothing has moved.
    /// </summary>
    private async Task<SavegameCompatibilityVerdict?> AssessAsync(
        Repo repo,
        SavegameDto savegame,
        int? playedRevision,
        CancellationToken cancellationToken)
    {
        if (playedRevision is not int played
            || repo.Adapter.FindSavegameCompatibility() is not SavegameCompatibilityPolicy policy
            || profileService.FindLive(repo.Id, savegame.ProfileId) is not ProfileDto profile)
        {
            return null;
        }

        return await compatibilityCheck.AssessAsync(repo.Id, profile.Id, played, profile.HeadRevision, policy, cancellationToken);
    }

    /// <summary>
    /// Latest, unless the mod list has moved far enough to ask. Null where the user backed out.
    /// </summary>
    private async Task<SavegameRevisionMode?> ChooseRevisionModeAsync(
        SavegameDto savegame,
        int? playedRevision,
        SavegameCompatibilityVerdict? verdict)
    {
        if (verdict is not { ShouldPrompt: true } || playedRevision is not int played)
        {
            return SavegameRevisionMode.Latest;
        }

        var modal = new SavegameCompatibilityModalViewModel(
            savegame.Name,
            SavegameWording.ProfileOf(savegame),
            played,
            verdict.Comparison.To,
            verdict);

        await modalService.Value.Show(modal);

        return modal.Result;
    }

    /// <summary>
    /// The way out of a refused slot: check the savegame occupying it in, then offer the modal again
    /// with the slot free.
    /// </summary>
    private async Task CheckInBlockingAsync(
        Repo repo,
        Game game,
        Guid blockingSavegameId,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode revisionMode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        var blockingName = nameOf(blockingSavegameId);

        var outcome = await checkInFlow.CheckInAsync(
            game,
            blockingSavegameId,
            blockingName ?? "that savegame",
            blockingName ?? "the slot",
            cancellationToken,
            // Null rather than the placeholder above: a name only worth showing in a sentence is not one
            // worth writing into the save.
            renameTo: blockingName);

        if (outcome.ReleasedTheSlot is false)
        {
            toasts.Show(
                outcome.WasDeferred
                    ? "That savegame was left checked out, so its slot is still taken."
                    : "That savegame is still checked out, so its slot is still taken.",
                ToastSeverity.Warning);

            return;
        }

        // The slot is no longer claimed but not empty either, so the modal about to open again offers
        // it as an unrecognised save - which needs saying, or it reads as the check-in having done nothing.
        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, blockingName is null ? "its slot" : $"'{blockingName}'s slot") is string notRecycled)
        {
            toasts.Show(notRecycled, ToastSeverity.Warning);
        }

        await changed();

        // Read again: a check-in a moment ago is exactly the kind of thing that moves the savegame's state.
        var refreshed = (await savegamesClient.GetSavegamesV1Async(repo.Id, cancellationToken))
            .FirstOrDefault(x => x.Id == savegame.Id);

        if (refreshed is not null)
        {
            await StartAsync(
                repo, refreshed, snapshotNumber, playedRevision, mode, revisionMode, currentUserId, nameOf, changed,
                agreedToTakeFrom, cancellationToken);
        }
    }

    private async Task ExecuteAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        SavegameRevisionMode revisionMode,
        Game game,
        SavegameSlotOptionViewModel slot,
        Func<Task> changed,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        var name = savegame.Name;

        using var task = backgroundTasks.Begin(
            mode is SavegameCheckOutMode.TakeCopy
                ? $"Copying '{name}' into '{game.Name}'"
                : $"Checking '{name}' out into '{game.Name}'",
            $"Snapshot {snapshotNumber}");

        task.DeclareTransfers(TransferDirection.Download);

        if (mode is SavegameCheckOutMode.TakeCopy)
        {
            var displacedByCopy = await savegameCheckOut.TakeCopyAsync(
                game, savegame, snapshotNumber, slot.Ref, cancellationToken, new SavegameStripProgress(task));

            toasts.Show($"Snapshot {snapshotNumber} of '{name}' is in '{game.Name}'. Nobody was stopped from playing it, " +
                        "and this machine holds no claim on it - the slot is an ordinary save of your own now.");

            ReportDisplaced(displacedByCopy);

            return;
        }

        // Restoring copies forward, so the check-out that follows has no stale base. Nothing in
        // between is deleted.
        if (snapshotNumber != savegame.Head?.Number)
        {
            task.Report($"Restoring snapshot {snapshotNumber} as the newest one");

            await savegamesClient.RestoreSavegameSnapshotV1Async(
                repo.Id, savegame.Id, snapshotNumber, new RestoreSavegameSnapshotRequest(), cancellationToken);

            var refreshed = await savegamesClient.GetSavegamesV1Async(repo.Id, cancellationToken);

            savegame = refreshed.FirstOrDefault(x => x.Id == savegame.Id) ?? savegame;
        }

        task.Report("Taking the claim");

        var (takenFrom, displaced) = await savegameCheckOut.CheckOutAsync(game, savegame, slot.Ref, revisionMode, cancellationToken, new SavegameStripProgress(task));

        ReportDisplaced(displaced);

        if (takenFrom is null)
        {
            toasts.Show($"'{name}' is checked out to you, in '{game.Name}'.");
        }
        else if (takenFrom.UserId == agreedToTakeFrom)
        {
            toasts.Show($"'{name}' is checked out to you, in '{game.Name}'. {takenFrom.DisplayName} no longer has it, " +
                        "and their ModsDude will tell them.");
        }
        else
        {
            // Somebody took the save after the list was read, so the take-over question was never
            // asked about them. The claim is taken by now; all that is left is to say whose it was.
            toasts.Show($"{takenFrom.DisplayName} had '{name}' checked out since {SavegameWording.Exactly(takenFrom.TakenAt)} - " +
                        $"the list you started from did not show it yet. It is yours now, in '{game.Name}', and their ModsDude will tell them.",
                        ToastSeverity.Warning);
        }

        await profileActivation.ActivateCheckedOutAsync(repo, game, savegame, cancellationToken);

        await changed();
    }

    /// <summary>Where the save a check-out or copy replaced went, where that is worth saying.</summary>
    private void ReportDisplaced(DisplacedSavegame? displaced)
    {
        switch (displaced?.Destination)
        {
            case DisplacedSavegameDestination.QuarantineFolder:
                toasts.Show(
                    $"The save that was in that slot could not go to the Recycle Bin, so it was moved to {displaced.Path}.",
                    ToastSeverity.Warning);
                break;

            case DisplacedSavegameDestination.BesideSlots:
                toasts.Show(
                    $"The save that was in that slot could not be moved to the Recycle Bin, so it is still on disk as {displaced.Path}.",
                    ToastSeverity.Warning);
                break;
        }
    }
}
