using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.Profiles.Editor;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
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
    IProfileModPinsClient pinsClient,
    IModStore modStore,
    IShellNavigationService shellNavigation,
    ILogger<SavegamePublishWizard> wizardLogger,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IToastService toasts) : ISavegamePublishFlow
{
    public async Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        SavegameSlotRef? preselectSlot,
        Action<Guid>? published,
        CancellationToken cancellationToken)
    {
        if (repo.Games.FirstOrDefault() is not Game game)
        {
            await modalService.Value.Show(SavegameRefusals.NotConnected(repo));

            return;
        }

        SavegamePublishOutcome? outcome;

        try
        {
            outcome = await AskAsync(repo, game, preselectProfileId, preselectSlot, cancellationToken);
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

        switch (outcome)
        {
            case SavegamePublishOutcome.Publish publish:
                await RunAsync(repo, game, publish.Plan, published);
                break;

            case SavegamePublishOutcome.ReviewInEditor review:
                await shellNavigation.GoToAsync(repo.Id, new RepoDestination.ProfileMods(review.ProfileId, new EditorFocus.Savegame(game.Identity, review.Slot)));
                break;

            case SavegamePublishOutcome.CreateProfile create:
                await CreateProfileAsync(repo, game, create.Slot, cancellationToken);
                break;
        }
    }

    private Task RunAsync(Repo repo, Game game, SavegamePublishPlan plan, Action<Guid>? published)
        => operations.RunAsync(
            new SavegameOperation(
                game,
                $"Publishing '{plan.Name}' to {repo.Name}",
                "publishing a savegame",
                $"Publishing '{plan.Name}' cancelled",
                plan.CheckInFirst is SavegamePublishCheckIn checkIn ? [checkIn.SavegameId] : [],
                AllFolders: true)
            {
                Detail = $"Packing and uploading '{plan.Slot.Label}'"
            },
            (task, ct) => ExecuteAsync(game, repo, plan, published, task, ct));

    /// <summary>
    /// Asks for a name, makes an empty profile of it, and opens its editor with the savegame's mods
    /// added. The savegame is published from there, once the profile is saved.
    /// </summary>
    private async Task CreateProfileAsync(Repo repo, Game game, SavegameSlotOptionViewModel slot, CancellationToken cancellationToken)
    {
        // One per name asked for in this modal, so a retry after a lost answer finds the profile it made.
        var requestIds = new RequestIdKeeper<string>();
        Profile? created = null;

        try
        {
            await NameModalViewModel.AskAsync(
                modalService.Value,
                "Create profile",
                $"From the mods of '{slot.Label}'.",
                slot.Label,
                "Create",
                async name =>
                {
                    try
                    {
                        created = await profileStore.CreateAsync(repo.Id, requestIds.For(name), name, copyFrom: null, cancellationToken);

                        return null;
                    }
                    catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.NameTaken)
                    {
                        return "That name is taken.";
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "creating a profile from a savegame");

            return;
        }

        if (created is not null)
        {
            await shellNavigation.GoToAsync(repo.Id, new RepoDestination.ProfileMods(created.Id, new EditorFocus.Savegame(game.Identity, slot.Ref)));
        }
    }

    /// <summary>The questions, as one wizard. Null where there is nothing to publish or the user backed out.</summary>
    private async Task<SavegamePublishOutcome?> AskAsync(
        Repo repo, Game game, Guid? preselectProfileId, SavegameSlotRef? preselectSlot, CancellationToken cancellationToken)
    {
        var slots = await ReadPublishableSlotsAsync(game, cancellationToken);

        if (slots.Count == 0)
        {
            await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                "Nothing to publish",
                "Every slot is empty or already published."));

            return null;
        }

        var wizard = new SavegamePublishWizard(
            repo, game, preselectProfileId, preselectSlot, slots, savegameSlots, heldSavegames, profileStore, manifestStore,
            checkInFlow, heldSavegameNames, pinsClient, modStore, wizardLogger);

        var modal = new WizardModalViewModel(wizard.First, wizard.NextAsync, cancellationToken);

        return await modal.ShowAsync(modalService.Value) ? wizard.Outcome : null;
    }

    /// <summary>
    /// The slots holding bytes ModsDude has no copy of. An empty slot has nothing to publish, and a
    /// checked-out save is checked in rather than published again.
    /// </summary>
    private async Task<IReadOnlyList<SavegameSlotOptionViewModel>> ReadPublishableSlotsAsync(
        Game game, CancellationToken cancellationToken)
    {
        return
        [
            .. (await savegameSlots.ClassifySlotsAsync(game, cancellationToken))
                .Where(x => x.Availability is SavegameSlotAvailability.Unrecognised)
                .Select(x => new SavegameSlotOptionViewModel(x.Slot, x.Availability))
        ];
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
            toasts.Show($"Published '{outcome.Savegame.Name}' to {repo.Name}. {notRecycled}", ToastSeverity.Warning);
        }
        else
        {
            toasts.Show(outcome.LocalCopy is SavegameLocalCopy.Kept
                ? $"Published '{outcome.Savegame.Name}' to {repo.Name}. It is checked out to you."
                : $"Published '{outcome.Savegame.Name}' to {repo.Name}. The local copy is in the Recycle Bin.");
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
                toasts.Show($"'{plan.Name}' was not published. {checkIn.Savegame.Subject} is still checked out.", ToastSeverity.Warning);
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
