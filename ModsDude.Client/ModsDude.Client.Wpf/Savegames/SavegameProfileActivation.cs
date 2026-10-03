using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegameProfileActivation(
    ISavegameOffers offers,
    IProfileService profileService,
    IProfileApplyService applyService,
    IDriftMonitor driftMonitor,
    IShellNavigationService shellNavigation,
    Lazy<IModalService> modalService,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts) : ISavegameProfileActivation
{
    public async Task<bool> ConfirmActivateFirstAsync(
        Repo repo,
        Game game,
        SavegameDto savegame,
        SavegameCheckOutMode mode,
        int? pinnedRevision,
        Func<Task> changed,
        CancellationToken cancellationToken)
    {
        // The same rule the row drew its button from, read again: the folder may have moved since.
        if (repo.Adapter.CanSupportMods is false
            || profileService.FindLive(repo.Id, savegame.ProfileId) is not ProfileDto profile
            || offers.ReadHost(repo) is not SavegameHost host)
        {
            return true;
        }

        var offer = SavegameRowRules.Describe(
            savegame.Id, profile.Id, profile.HeadRevision, pinnedRevision, host.Held, host.AppliedProfileId, host.AppliedRevision);

        if (offer.ActivatesFirst is false)
        {
            return true;
        }

        var list = SavegameRowRules.DescribeActivation(profile.Name, pinnedRevision);

        // A check-out holds the save against this folder, so the folder has to be right. A copy claims
        // nothing, so writing it next to whatever the folder has now is the user's choice.
        var confirmation = mode is SavegameCheckOutMode.TakeCopy
            ? new ConfirmationModalViewModel(
                $"Activate {list} first?",
                $"'{savegame.Name}' runs on {list}, and the mod folder in '{game.Name}' is not on it. "
                    + $"Activating it first puts the mods the copy was saved with in place. Leaving it writes the copy "
                    + "next to whatever mods the folder has now, which the game may not load it with.",
                IconKind.Question,
                "Activate it, then take the copy",
                "Cancel",
                "Leave the mods as they are")
            : new ConfirmationModalViewModel(
                $"Activate {list} first?",
                $"'{savegame.Name}' runs on {list}, and the mod folder in '{game.Name}' is not on it. "
                    + $"Checking it out activates {list} first, then asks which slot to write the save into.",
                IconKind.Question,
                "Activate it, then check out",
                "Cancel");

        await modalService.Value.Show(confirmation);

        if (confirmation.ChoseAlternative)
        {
            return true;
        }

        if (confirmation.Result is false)
        {
            return false;
        }

        // Named, not left to the game: nothing is holding this savegame yet, so the game would resolve
        // head - wrong in compatibility mode, whose check-out would then leave the folder drifted.
        var outcome = await applyService.ActivateAsync(
            repo, game, profile.Id, profile.Name, confirmPlan: false, progress: null, cancellationToken,
            revision: pinnedRevision ?? profile.HeadRevision);

        await driftMonitor.CheckAsync();

        // The folder moved, so every row's answer about it has too - including where the check-out
        // is abandoned at the slot modal that follows.
        await changed();

        if (outcome.Succeeded is false)
        {
            toasts.Show(outcome.Message, outcome.ToastSeverity);

            return false;
        }

        return true;
    }

    public async Task ActivateCheckedOutAsync(Repo repo, Game game, SavegameDto savegame, CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false)
        {
            return;
        }

        if (profileService.FindLive(repo.Id, savegame.ProfileId) is not ProfileDto profile)
        {
            return;
        }

        // The revision is not decided here: the binding written by the check-out carries it, and every
        // apply resolves it from there, so this installs the list the drift check then expects.
        // Named as a check-out so friends hear about it as one, even where nothing about the
        // activation itself is news.
        var outcome = await applyService.ActivateAsync(
            repo, game, profile.Id, profile.Name, confirmPlan: false, progress: null, cancellationToken,
            checkedOutSavegame: savegame.Id);

        toasts.Show(outcome.Message, outcome.ToastSeverity);

        await driftMonitor.CheckAsync();

        // Declined only, not stopped: a Cancel on the strip says nothing about the files in the folder.
        if (outcome.Status is ProfileApplyStatus.Declined)
        {
            await OfferModListReviewAsync(repo, game, profile, cancellationToken);
        }
    }

    /// <summary>
    /// The way out of a declined apply: open the profile's mod list with the folder that stopped it
    /// already scanned, where unrecognised mods can be imported instead of recycled. Asks nothing
    /// where the folder has none.
    /// </summary>
    private async Task OfferModListReviewAsync(Repo repo, Game game, ProfileDto profile, CancellationToken cancellationToken)
    {
        // On the strip because planning reads and hashes the mod folder, between two modals.
        using var task = backgroundTasks.Begin($"Checking what '{profile.Name}' would change");

        var plans = (await applyService.TryPlanAsync(
            repo, game, profile.Id, profile.Name, revision: null, cancellationToken, ProfileApplyService.Report(task, null))).Plans;

        if (plans.FirstOrDefault(x => x.Unrecognised.Count > 0) is not ModSyncPlan plan)
        {
            return;
        }

        var choice = new ConfirmationModalViewModel(
            $"Open '{profile.Name}'s mod list?",
            $"{plan.Unrecognised.Count} mods in the mod folder are not in this repo, and applying is what moves them to "
                + "the Recycle Bin. The mod list is where they get imported instead - and until something is applied, "
                + "the save you just took is on a mod list the folder is not on.",
            IconKind.Question,
            "Review - opens the mod list with this folder scanned",
            "Not now");

        await modalService.Value.Show(choice);

        if (choice.Result)
        {
            await shellNavigation.GoToProfileModsAsync(repo.Id, profile.Id, plan.TargetRef);
        }
    }
}
