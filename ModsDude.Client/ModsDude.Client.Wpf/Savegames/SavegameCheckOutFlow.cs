using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
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
    ISavegameStore store,
    ISavegameCheckInFlow checkInFlow,
    ISavegameProfileActivation profileActivation,
    ISavegameCheckOutContextBuilder contextBuilder,
    ISavegameOffers offers,
    ISavegameBindingStore bindings,
    ISavegameCompatibilityCheck compatibilityCheck,
    IProfileStore profileStore,
    IGameRunningGuard runningGuard,
    ISavegameOperations operations,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IToastService toasts) : ISavegameCheckOutFlow
{
    // Not keyed on the head: after a lost answer the head shown is the restore's own.
    private readonly RequestIdKeeper<(Guid SavegameId, int SnapshotNumber)> _restoreRequestIds = new();

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

        SavegameCheckOutPlan? plan;

        try
        {
            plan = await AskAsync(repo, game, savegame, snapshotNumber, playedRevision, mode, revisionMode, currentUserId, heldNames, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page asking went away before anything was answered. Nothing has started.
            return;
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "checking a savegame out");

            return;
        }

        if (plan is null)
        {
            return;
        }

        var name = savegame.Name;

        await operations.RunAsync(
            new SavegameOperation(
                game,
                mode is SavegameCheckOutMode.TakeCopy ? $"Copying '{name}' into '{game.Name}'" : $"Checking '{name}' out into '{game.Name}'",
                mode is SavegameCheckOutMode.TakeCopy ? "copying a savegame" : "checking a savegame out",
                mode is SavegameCheckOutMode.TakeCopy
                    ? $"Copying '{name}' stopped. Nothing was written into a slot."
                    : $"Checking '{name}' out stopped before anything was written into a slot. Where it already shows as yours, Check out again finishes it.",
                [savegame.Id, .. plan.CheckIns.Select(x => x.SavegameId)],
                AllFolders: true)
            {
                Detail = $"Snapshot {snapshotNumber}"
            },
            (task, ct) => ExecuteAsync(repo, game, savegame, snapshotNumber, mode, plan, task, ct));
    }

    /// <summary>The questions, as one wizard. Null where the user backed out of it.</summary>
    private async Task<SavegameCheckOutPlan?> AskAsync(
        Repo repo,
        Game game,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? revisionMode,
        string? currentUserId,
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
        CancellationToken cancellationToken)
    {
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

        return await modal.ShowAsync(modalService.Value) ? wizard.Plan : null;
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
    private async Task ExecuteAsync(
        Repo repo,
        Game game,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        SavegameCheckOutPlan plan,
        IBackgroundTask task,
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
                if (await CheckInFirstAsync(game, savegame, checkIn, task, cancellationToken) is false)
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

        await WriteAsync(repo, savegame, snapshotNumber, mode, plan.RevisionMode, game, plan.Slot, task, cancellationToken);
    }

    /// <returns>Whether the savegame was handed back, which is what frees the folder or slot it held.</returns>
    private async Task<bool> CheckInFirstAsync(
        Game game,
        SavegameDto savegame,
        SavegameCheckOutCheckIn checkIn,
        IBackgroundTask task,
        CancellationToken cancellationToken)
    {
        var outcome = await checkInFlow.CheckInAsync(
            game, checkIn.SavegameId, checkIn.Savegame, checkIn.Label, keepPlaying: false, task, cancellationToken);

        if (outcome.ReleasedTheSlot is false)
        {
            // A check-in that failed has already said so.
            if (outcome.WasDeferred || outcome.Succeeded)
            {
                toasts.Show($"{SavegameSlotWording.Capitalised(checkIn.Savegame.Quoted)} is still checked out, so '{savegame.Name}' was not checked out.", ToastSeverity.Warning);
            }

            return false;
        }

        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, $"the slot of {checkIn.Savegame.Quoted}", game.Name) is string notRecycled)
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
        IBackgroundTask task,
        CancellationToken cancellationToken)
    {
        var name = savegame.Name;

        task.DeclareTransfers(TransferDirection.Download);

        if (mode is SavegameCheckOutMode.TakeCopy)
        {
            task.Report($"Copying snapshot {snapshotNumber}");

            var displacedByCopy = await savegameCheckOut.TakeCopyAsync(
                game, savegame, snapshotNumber, slot.Ref, cancellationToken, new SavegameStripProgress(task));

            toasts.Show($"Snapshot {snapshotNumber} of '{name}' is in '{game.Name}'. Nobody was stopped from playing it, " +
                        "and this machine holds no claim on it - the slot is an ordinary save of your own now.");

            ReportDisplaced(displacedByCopy);

            return;
        }

        task.Report(snapshotNumber != savegame.Head?.Number
            ? $"Restoring snapshot {snapshotNumber} as the newest one, and taking the claim"
            : "Taking the claim");

        var result = await savegameCheckOut.CheckOutAsync(
            game,
            savegame,
            snapshotNumber,
            _restoreRequestIds.For((savegame.Id, snapshotNumber)),
            slot.Ref,
            revisionMode,
            cancellationToken,
            new SavegameStripProgress(task));

        _restoreRequestIds.Forget();

        ReportDisplaced(result.Displaced);

        if (result.PreviousCopy is SavegameLocalCopy previous
            && SavegameSlotWording.NotRecycled(previous, "the slot it moved out of", game.Name) is string notRecycled)
        {
            toasts.Show(notRecycled, ToastSeverity.Warning);
        }

        if (result.TakenFrom is null)
        {
            toasts.Show($"'{name}' is checked out to you, in '{game.Name}'.");
        }
        else
        {
            toasts.Show($"'{name}' is checked out to you, in '{game.Name}'. {result.TakenFrom.DisplayName} no longer has it, " +
                        "and their ModsDude will tell them.");
        }

        await profileActivation.ActivateCheckedOutAsync(repo, game, result.Savegame, cancellationToken);
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
