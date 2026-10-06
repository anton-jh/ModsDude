using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegamePublishFlow(
    ISavegameSlots savegameSlots,
    ISavegamePublisher savegamePublisher,
    IHeldSavegames heldSavegames,
    IProfileService profileService,
    ISyncManifestStore manifestStore,
    ISavegameCheckInFlow checkInFlow,
    ISavegameProfileActivation profileActivation,
    ISavegamesClient savegamesClient,
    IGameRunningGuard runningGuard,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts) : ISavegamePublishFlow
{
    public async Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Func<Guid?, Task> changed,
        CancellationToken cancellationToken)
    {
        if (repo.Games.FirstOrDefault() is not Game game)
        {
            await modalService.Value.Show(SavegameRefusals.NotConnected(
                repo,
                "Publishing takes a save that is already on this machine, so there has to be an installation of the game to take one from."));

            return;
        }

        try
        {
            var slots = await ReadPublishableSlotsAsync(game, cancellationToken);

            if (slots.Count == 0)
            {
                await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                    "There is nothing here to publish",
                    "Every slot is either empty or holds a savegame ModsDude already has a copy of. A checked-out save is checked in rather than published again, which is the button on its row in the repo's saves list."));

                return;
            }

            var wizard = new SavegamePublishWizard(
                repo, game, preselectProfileId, slots, savegameSlots, heldSavegames, profileService, manifestStore, checkInFlow, savegamesClient);

            var modal = new WizardModalViewModel(wizard.First, wizard.NextAsync, cancellationToken);

            if (await modal.ShowAsync(modalService.Value) is false || wizard.Plan is not SavegamePublishPlan plan)
            {
                return;
            }

            await ExecuteAsync(game, repo, plan, changed, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "publishing a savegame");
        }
    }

    /// <summary>
    /// The slots holding bytes ModsDude has no copy of. An empty slot has nothing to publish, and a
    /// checked-out save is checked in rather than published again.
    /// </summary>
    private async Task<IReadOnlyList<SavegameSlotOptionViewModel>> ReadPublishableSlotsAsync(
        Game game, CancellationToken cancellationToken)
    {
        var options = new List<SavegameSlotOptionViewModel>();

        foreach (var slot in await savegameSlots.GetSlotsAsync(game, cancellationToken))
        {
            var availability = await savegameSlots.ClassifySlotAsync(game, slot.Ref, cancellationToken);

            if (availability is SavegameSlotAvailability.Unrecognised)
            {
                options.Add(new SavegameSlotOptionViewModel(slot, availability));
            }
        }

        return options;
    }

    /// <summary>
    /// The work the wizard was answered with, in order: check in the savegame holding the mod folder,
    /// activate the profile, publish. Each step stops the rest where it does not finish.
    /// </summary>
    private async Task ExecuteAsync(
        Game game,
        Repo repo,
        SavegamePublishPlan plan,
        Func<Guid?, Task> changed,
        CancellationToken cancellationToken)
    {
        // Before the first step, since an activation into a running game would be recorded and then
        // left drifted.
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        if (plan.CheckInFirst is SavegamePublishCheckIn checkIn)
        {
            var released = await CheckInFirstAsync(game, plan, checkIn, cancellationToken);

            await changed(null);

            if (released is false)
            {
                return;
            }
        }

        if (plan.ActivatesFirst
            && plan.Profile.ProfileId is Guid profileId
            && await profileActivation.ActivateFirstAsync(repo, game, profileId, plan.Profile.Name, revision: null, cancellationToken) is false)
        {
            await changed(null);

            return;
        }

        var outcome = await PublishSlotAsync(game, repo, plan, cancellationToken);

        // The slot is in a different state in each ending, and the sentence is the only thing that
        // says which.
        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, SavegameSlotWording.Named(savegameSlots.DescribeSlotNumber(game, plan.Slot.Ref), outcome.Savegame.Name))
            is string notRecycled)
        {
            toasts.Show($"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. {notRecycled}", ToastSeverity.Warning);
        }
        else
        {
            toasts.Show(outcome.LocalCopy is SavegameLocalCopy.Kept
                ? $"'{outcome.Savegame.Name}' is in {repo.Name}, and checked out to you."
                : $"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. The local copy went to the Recycle Bin.");
        }

        await changed(outcome.Savegame.Id);
    }

    /// <returns>Whether the savegame was handed back, which is what frees the mod folder.</returns>
    private async Task<bool> CheckInFirstAsync(
        Game game,
        SavegamePublishPlan plan,
        SavegamePublishCheckIn checkIn,
        CancellationToken cancellationToken)
    {
        var outcome = await checkInFlow.CheckInAsync(
            game, checkIn.SavegameId, checkIn.Name, checkIn.Label, keepPlaying: false, cancellationToken, renameTo: checkIn.RenameTo);

        if (outcome.ReleasedTheSlot is false)
        {
            // A check-in that failed has already said so.
            if (outcome.WasDeferred || outcome.Succeeded)
            {
                toasts.Show($"'{checkIn.Name}' is still checked out, so '{plan.Name}' was not published.", ToastSeverity.Warning);
            }

            return false;
        }

        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, $"'{checkIn.Name}'s slot") is string notRecycled)
        {
            toasts.Show(notRecycled, ToastSeverity.Warning);
        }

        return true;
    }

    private async Task<SavegamePublishResult> PublishSlotAsync(
        Game game,
        Repo repo,
        SavegamePublishPlan plan,
        CancellationToken cancellationToken)
    {
        using var task = backgroundTasks.Begin(
            $"Publishing '{plan.Name}' to {repo.Name}",
            plan.KeepPlaying
                ? $"Packing and uploading '{plan.Slot.Label}'"
                : $"Packing and uploading '{plan.Slot.Label}', then handing it back");

        task.DeclareTransfers(TransferDirection.Upload);

        return await savegamePublisher.PublishAsync(
            game, repo.Id, plan.Slot.Ref, plan.Name, plan.Label, plan.Profile.ToTarget(), plan.KeepPlaying, cancellationToken,
            new SavegameStripProgress(task));
    }
}
