using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
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
    IProfileStore profileStore,
    ISyncManifestStore manifestStore,
    ISavegameCheckInFlow checkInFlow,
    ISavegameProfileActivation profileActivation,
    IHeldSavegameNames heldSavegameNames,
    IGameRunningGuard runningGuard,
    ISavegameOperations operations,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IToastService toasts) : ISavegamePublishFlow
{
    public async Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Action<Guid>? published,
        CancellationToken cancellationToken)
    {
        if (repo.Games.FirstOrDefault() is not Game game)
        {
            await modalService.Value.Show(SavegameRefusals.NotConnected(
                repo,
                "Publishing takes a save that is already on this machine, so there has to be an installation of the game to take one from."));

            return;
        }

        SavegamePublishPlan? plan;

        try
        {
            plan = await AskAsync(repo, game, preselectProfileId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page asking went away before anything was answered. Nothing has started.
            return;
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "publishing a savegame");

            return;
        }

        if (plan is null)
        {
            return;
        }

        await operations.RunAsync(
            new SavegameOperation(
                game,
                $"Publishing '{plan.Name}' to {repo.Name}",
                "publishing a savegame",
                $"Publishing '{plan.Name}' stopped. Your save is still in its slot.",
                plan.CheckInFirst is SavegamePublishCheckIn checkIn ? [checkIn.SavegameId] : [],
                AllFolders: true)
            {
                Detail = plan.KeepPlaying
                    ? $"Packing and uploading '{plan.Slot.Label}'"
                    : $"Packing and uploading '{plan.Slot.Label}', then handing it back"
            },
            (task, ct) => ExecuteAsync(game, repo, plan, published, task, ct));
    }

    /// <summary>The questions, as one wizard. Null where there is nothing to publish or the user backed out.</summary>
    private async Task<SavegamePublishPlan?> AskAsync(Repo repo, Game game, Guid? preselectProfileId, CancellationToken cancellationToken)
    {
        var slots = await ReadPublishableSlotsAsync(game, cancellationToken);

        if (slots.Count == 0)
        {
            await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                "There is nothing here to publish",
                "Every slot is either empty or holds a savegame ModsDude already has a copy of. A checked-out save is checked in rather than published again, which is the button on its row in the repo's saves list."));

            return null;
        }

        var wizard = new SavegamePublishWizard(
            repo, game, preselectProfileId, slots, savegameSlots, heldSavegames, profileStore, manifestStore, checkInFlow, heldSavegameNames);

        var modal = new WizardModalViewModel(wizard.First, wizard.NextAsync, cancellationToken);

        return await modal.ShowAsync(modalService.Value) ? wizard.Plan : null;
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
    /// The work the wizard was answered with, in order: download the mods of the profile it activates,
    /// check in the savegame holding the mod folder, activate the profile, publish. Each step stops the
    /// rest where it does not finish.
    /// </summary>
    private async Task ExecuteAsync(
        Game game,
        Repo repo,
        SavegamePublishPlan plan,
        Action<Guid>? published,
        IBackgroundTask task,
        CancellationToken cancellationToken)
    {
        // Before the first step, since an activation into a running game would be recorded and then
        // left drifted.
        runningGuard.EnsureNotRunning(game.Identity, game.Name);

        if (plan.ActivatesFirst
            && plan.Profile.ProfileId is Guid activating
            && await profileActivation.FetchModsFirstAsync(
                repo, game, activating, plan.Profile.Name, revision: null, $"'{plan.Name}' was not published.", cancellationToken) is false)
        {
            return;
        }

        if (plan.CheckInFirst is SavegamePublishCheckIn checkIn)
        {
            if (await CheckInFirstAsync(game, plan, checkIn, task, cancellationToken) is false)
            {
                return;
            }
        }

        if (plan.ActivatesFirst
            && plan.Profile.ProfileId is Guid profileId
            && await profileActivation.ActivateFirstAsync(repo, game, profileId, plan.Profile.Name, revision: null, cancellationToken) is false)
        {
            return;
        }

        task.Report($"Publishing '{plan.Name}'");
        task.DeclareTransfers(TransferDirection.Upload);

        var outcome = await savegamePublisher.PublishAsync(
            game, repo.Id, plan.Slot.Ref, plan.Name, plan.Label, plan.Profile.ToTarget(), plan.KeepPlaying, cancellationToken,
            new SavegameStripProgress(task));

        // The slot is in a different state in each ending, and the sentence is the only thing that
        // says which.
        var slot = SavegameSlotWording.Named(savegameSlots.DescribeSlotNumber(game, plan.Slot.Ref), outcome.Savegame.Name);

        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, slot, game.Name) is string notRecycled)
        {
            toasts.Show($"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. {notRecycled}", ToastSeverity.Warning);
        }
        else
        {
            toasts.Show(outcome.LocalCopy is SavegameLocalCopy.Kept
                ? $"'{outcome.Savegame.Name}' is in {repo.Name}, and checked out to you."
                : $"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. The local copy went to the Recycle Bin.");
        }

        published?.Invoke(outcome.Savegame.Id);
    }

    /// <returns>Whether the savegame was handed back, which is what frees the mod folder.</returns>
    private async Task<bool> CheckInFirstAsync(
        Game game,
        SavegamePublishPlan plan,
        SavegamePublishCheckIn checkIn,
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
                toasts.Show($"{SavegameSlotWording.Capitalised(checkIn.Savegame.Quoted)} is still checked out, so '{plan.Name}' was not published.", ToastSeverity.Warning);
            }

            return false;
        }

        if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, $"the slot of {checkIn.Savegame.Quoted}", game.Name) is string notRecycled)
        {
            toasts.Show(notRecycled, ToastSeverity.Warning);
        }

        return true;
    }
}
