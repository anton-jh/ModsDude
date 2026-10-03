using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
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
    IProfileService profileService,
    ISyncManifestStore manifestStore,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts) : ISavegamePublishFlow
{
    public async Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Func<Guid, Task> published,
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

            var picker = new SavegameSlotPickerModalViewModel(repo.Name, slots);

            await modalService.Value.Show(picker);

            if (picker.Result is not SavegameSlotOptionViewModel chosen)
            {
                return;
            }

            var outcome = await PublishSlotAsync(game, repo, chosen.Ref, chosen.Label, preselectProfileId, cancellationToken);

            if (outcome is null)
            {
                return;
            }

            // The slot is in a different state in each ending, and the sentence is the only thing that
            // says which.
            if (SavegameSlotWording.NotRecycled(outcome.LocalCopy, SavegameSlotWording.Named(savegameSlots.DescribeSlotNumber(game, chosen.Ref), outcome.Savegame.Name))
                is string notRecycled)
            {
                toasts.Show($"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. {notRecycled}", ToastSeverity.Warning);
            }
            else
            {
                toasts.Show(outcome.KeptPlaying
                    ? $"'{outcome.Savegame.Name}' is in {repo.Name}, and checked out to you. " +
                      "The save has not moved - check it in when you want somebody else to be able to take it."
                    : $"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. The local copy went to the " +
                      "Recycle Bin - check it out again once the game is on that mod list.");
            }

            await published(outcome.Savegame.Id);
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

    /// <returns>What was created and whether it is still held, or null where the modal was dismissed.</returns>
    private async Task<SavegamePublishOutcome?> PublishSlotAsync(
        Game game,
        Repo repo,
        SavegameSlotRef slot,
        string slotLabel,
        Guid? preselectProfileId,
        CancellationToken cancellationToken)
    {
        // This slot's own folder: the first snapshot's revision is a declaration about the mods that
        // were beside these bytes.
        var manifest = manifestStore.TryRead(new ModTargetRef(game.Identity, slot.Target));
        var options = await BuildOptionsAsync(repo, manifest?.ProfileId, manifest?.ProfileRevision, cancellationToken);

        // Which profile this game follows decides whether the save can be kept: a publish to any other
        // one hands it straight back.
        var activeProfileId = game.ActiveProfile is ActiveProfile profile && profile.RepoId == repo.Id
            ? profile.ProfileId
            : (Guid?)null;

        var preselected = preselectProfileId ?? activeProfileId;

        var modal = new SavegamePublishModalViewModel(
            slotLabel,
            repo.Name,
            slotLabel,
            options,
            options.FirstOrDefault(x => x.ProfileId == preselected && x.ProfileId is not null),
            activeProfileId,
            options.FirstOrDefault(x => x.ProfileId is not null && x.ProfileId == manifest?.ProfileId)?.Name,
            savegameSlots.DescribeSlotNumber(game, slot));

        await modalService.Value.Show(modal);

        if (modal.Result is not string name)
        {
            return null;
        }

        var keepPlaying = modal.KeepPlaying;

        using var task = backgroundTasks.Begin(
            $"Publishing '{name}' to {repo.Name}",
            keepPlaying
                ? $"Packing and uploading '{slotLabel}'"
                : $"Packing and uploading '{slotLabel}', then handing it back");

        task.DeclareTransfers(TransferDirection.Upload);

        var result = await savegamePublisher.PublishAsync(
            game, repo.Id, slot, name, modal.TrimmedLabel, modal.SelectedProfile?.ToTarget(), keepPlaying, cancellationToken,
            new SavegameStripProgress(task));

        return new SavegamePublishOutcome(result.Savegame, keepPlaying, result.LocalCopy);
    }

    /// <summary>
    /// Every profile in the repo as something the modal can offer, plus the no-mod-list answer last.
    /// </summary>
    private async Task<IReadOnlyList<SavegamePublishOption>> BuildOptionsAsync(
        Repo repo,
        Guid? appliedProfileId,
        int? appliedRevision,
        CancellationToken cancellationToken)
    {
        // The repo's Saves page is reachable without ever having opened a profile.
        if (profileService.Profiles.Any(x => x.RepoId == repo.Id) is false)
        {
            await profileService.RefreshProfiles(repo.Id, cancellationToken);
        }

        var options = new List<SavegamePublishOption>();

        foreach (var profile in profileService.Profiles.Where(x => x.RepoId == repo.Id).OrderBy(x => x.Name, NaturalOrder.Comparer))
        {
            options.Add(new SavegamePublishOption(
                profile.Id,
                profile.Name,
                SavegameRevisionRules.DeclaredRevisionFor(profile.Id, profile.HeadRevision, appliedProfileId, appliedRevision),
                profile.Id == appliedProfileId));
        }

        options.Add(SavegamePublishOption.NoModList);

        return options;
    }
}


/// <summary>
/// What a publish ended up doing: the savegame it made, and whether this machine still holds it.
/// </summary>
public sealed record SavegamePublishOutcome(SavegameDto Savegame, bool KeptPlaying, SavegameLocalCopy LocalCopy);
