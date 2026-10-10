using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegameProfileActivation(
    IProfileStore profileStore,
    IProfileApplyService applyService,
    IDriftMonitor driftMonitor,
    IShellNavigationService shellNavigation,
    Lazy<IModalService> modalService,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts) : ISavegameProfileActivation
{
    public async Task<bool> FetchModsFirstAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string profileName,
        int? revision,
        string notDone,
        CancellationToken cancellationToken)
    {
        if (await applyService.FetchModsAsync(repo, game, profileId, profileName, revision, cancellationToken) is not string problem)
        {
            return true;
        }

        toasts.Show($"{notDone} {problem}", ToastSeverity.Warning);

        return false;
    }

    public async Task<bool> ActivateFirstAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string profileName,
        int? revision,
        CancellationToken cancellationToken)
    {
        // The user already agreed to this activation as a step of what they are doing, so the plan is
        // not put to them again. Files the repo does not have are still asked about.
        var outcome = await applyService.ActivateAsync(
            repo, game, profileId, profileName, confirmPlan: false, progress: null, cancellationToken, revision: revision);

        await driftMonitor.CheckAsync();

        if (outcome.Succeeded is false)
        {
            toasts.Show(outcome.Message, outcome.ToastSeverity);
        }

        return outcome.Succeeded;
    }

    public async Task ActivateCheckedOutAsync(Repo repo, Game game, SavegameDto savegame, CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false)
        {
            return;
        }

        if (profileStore.Find(repo.Id, savegame.ProfileId) is not Profile profile)
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
    private async Task OfferModListReviewAsync(Repo repo, Game game, Profile profile, CancellationToken cancellationToken)
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
            await shellNavigation.GoToAsync(repo.Id, new RepoDestination.ProfileMods(profile.Id, plan.TargetRef));
        }
    }
}
