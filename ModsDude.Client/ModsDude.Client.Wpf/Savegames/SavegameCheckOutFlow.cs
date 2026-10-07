using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegameCheckOutFlow(
    ISavegameCheckOut savegameCheckOut,
    ISavegamesClient savegamesClient,
    ISavegameStore store,
    ISavegameCheckInFlow checkInFlow,
    ISavegameProfileActivation profileActivation,
    ISavegameCheckOutContextBuilder contextBuilder,
    ISavegameOffers offers,
    ISavegameBindingStore bindings,
    ISavegameCompatibilityCheck compatibilityCheck,
    IProfileStore profileStore,
    IGameRunningGuard runningGuard,
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
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
        CancellationToken cancellationToken)
    {
        try
        {
            await StartAsync(
                repo, savegame, snapshotNumber, playedRevision, mode, revisionMode, currentUserId, heldNames, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Navigated away. There is no page left to say anything on.
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "checking a savegame out");
        }
    }

    private async Task StartAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? revisionMode,
        string? currentUserId,
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
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

        // A copy takes nothing, and a claim of your own is not somebody else's.
        var holder = savegame.Checkout is SavegameCheckoutDto checkout && checkout.Status is not SavegameCheckoutStatus.Ended
            ? checkout
            : null;
        var heldByMe = holder is not null && currentUserId is not null && holder.User.Id == currentUserId;
        var takingFrom = mode is SavegameCheckOutMode.CheckOut && heldByMe is false ? holder : null;

        var wizard = new SavegameCheckOutWizard(
            repo,
            game,
            savegame,
            snapshotNumber,
            playedRevision,
            mode,
            // A save already held here keeps the mode it is held in: its own pin refuses any other
            // revision, so switching is checking it in and out again.
            HeldMode(game, savegame) ?? revisionMode,
            takingFrom,
            await AssessAsync(repo, savegame, playedRevision, cancellationToken),
            heldNames,
            offers,
            profileStore,
            contextBuilder,
            checkInFlow);

        var modal = new WizardModalViewModel(await wizard.FirstAsync(cancellationToken), wizard.NextAsync, cancellationToken);

        if (await modal.ShowAsync(modalService.Value) is false || wizard.Plan is not SavegameCheckOutPlan plan)
        {
            return;
        }

        await ExecuteAsync(repo, game, savegame, snapshotNumber, mode, plan, takingFrom?.User.Id, cancellationToken);
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
            || profileStore.Find(repo.Id, savegame.ProfileId) is not Profile profile)
        {
            return null;
        }

        return await compatibilityCheck.AssessAsync(repo.Id, profile.Id, played, profile.HeadRevision, policy, cancellationToken);
    }

    /// <summary>
    /// The work the wizard was answered with, in order: download the mods the save runs on, check in
    /// what holds the mod folder or the slot, activate the profile, then write the save. Each step
    /// stops the rest where it does not finish.
    /// </summary>
    /// <param name="agreedToTakeFrom">Whose claim the user agreed to take, so a later toast can tell it from a surprise.</param>
    private async Task ExecuteAsync(
        Repo repo,
        Game game,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        SavegameCheckOutPlan plan,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        // Before the first step, since an activation into a running game would be recorded and then
        // left drifted.
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        // A check-out activates the profile once the save is written, and a copy where it was asked to.
        var runsOn = mode is SavegameCheckOutMode.CheckOut && repo.Adapter.CanSupportMods
            ? profileStore.Find(repo.Id, savegame.ProfileId)
            : plan.Activates;

        if (runsOn is not null
            && await profileActivation.FetchModsFirstAsync(
                repo,
                game,
                runsOn.Id,
                runsOn.Name,
                plan.PinnedRevision ?? runsOn.HeadRevision,
                mode is SavegameCheckOutMode.TakeCopy ? $"'{savegame.Name}' was not copied." : $"'{savegame.Name}' was not checked out.",
                cancellationToken) is false)
        {
            return;
        }

        if (plan.CheckIns.Count > 0)
        {
            foreach (var checkIn in plan.CheckIns)
            {
                if (await CheckInFirstAsync(game, savegame, checkIn, cancellationToken) is false)
                {
                    return;
                }
            }

            savegame = store.Find(repo.Id, savegame.Id) ?? savegame;
        }

        // Named, not left to the game: nothing is holding this savegame yet, so the game would resolve
        // head - wrong in compatibility mode, whose check-out would then leave the folder drifted.
        if (plan.Activates is Profile profile
            && await profileActivation.ActivateFirstAsync(
                repo, game, profile.Id, profile.Name, plan.PinnedRevision ?? profile.HeadRevision, cancellationToken) is false)
        {
            return;
        }

        await WriteAsync(repo, savegame, snapshotNumber, mode, plan.RevisionMode, game, plan.Slot, agreedToTakeFrom, cancellationToken);
    }

    /// <returns>Whether the savegame was handed back, which is what frees the folder or slot it held.</returns>
    private async Task<bool> CheckInFirstAsync(
        Game game,
        SavegameDto savegame,
        SavegameCheckOutCheckIn checkIn,
        CancellationToken cancellationToken)
    {
        var outcome = await checkInFlow.CheckInAsync(
            game, checkIn.SavegameId, checkIn.Savegame, checkIn.Label, keepPlaying: false, cancellationToken);

        if (outcome.ReleasedTheSlot is false)
        {
            // A check-in that failed has already said so.
            if (outcome.WasDeferred || outcome.Succeeded)
            {
                toasts.Show($"{SavegameSlotWording.Capitalised(checkIn.Savegame.Quoted)} is still checked out, so '{savegame.Name}' was not checked out.", ToastSeverity.Warning);
            }

            return false;
        }

        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, $"the slot of {checkIn.Savegame.Quoted}") is string notRecycled)
        {
            toasts.Show(notRecycled, ToastSeverity.Warning);
        }

        return true;
    }

    private async Task WriteAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        SavegameRevisionMode revisionMode,
        Game game,
        SavegameSlotOptionViewModel slot,
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

            await store.WriteAsync(
                repo.Id,
                token => savegamesClient.RestoreSavegameSnapshotV1Async(repo.Id, savegame.Id, snapshotNumber, new RestoreSavegameSnapshotRequest(), token),
                cancellationToken);

            savegame = store.Find(repo.Id, savegame.Id) ?? savegame;
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
