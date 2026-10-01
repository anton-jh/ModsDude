using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;

namespace ModsDude.Client.Wpf.Savegames;

public sealed class SavegameCheckOutContextBuilder(
    ISavegameSlots savegameSlots,
    ISavegameBindingStore bindingStore,
    IProfileService profileService,
    IProfileApplyService applyService,
    ILockedPinDrift lockedPinDrift,
    IBackgroundTaskReporter backgroundTasks) : ISavegameCheckOutContextBuilder
{
    public async Task<SavegameCheckOutContext> BuildAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        SavegameCheckOutMode mode,
        Func<Guid, string?> nameOf,
        CancellationToken cancellationToken)
    {
        var slots = await savegameSlots.GetSlotsAsync(game, cancellationToken);
        var options = new List<SavegameSlotOptionViewModel>();

        foreach (var slot in slots)
        {
            var availability = await savegameSlots.ClassifySlotAsync(game, slot.Ref, cancellationToken);
            var binding = bindingStore.GetBindingForSlot(game.Identity, slot.Ref);

            options.Add(new SavegameSlotOptionViewModel(
                slot,
                availability,
                binding?.SavegameId,
                binding is SavegameCheckoutBinding held ? nameOf(held.SavegameId) : null));
        }

        var suggested = await savegameSlots.SuggestSlotAsync(game, savegame.Id, cancellationToken);
        var hint = bindingStore.GetSlotHint(game.Identity, savegame.Id);

        return new SavegameCheckOutContext(
            options,
            suggested,
            DescribeSuggestion(options, suggested, hint),
            mode is SavegameCheckOutMode.CheckOut
                ? await BuildModsSummaryAsync(repo, savegame, game, cancellationToken)
                : null,
            await BuildRevisionNoteAsync(repo, savegame, cancellationToken),
            // Absent for a copy, which applies nothing.
            mode is SavegameCheckOutMode.CheckOut ? DescribeRunsOn(repo, savegame) : null);
    }

    /// <summary>
    /// Which revision the folder will be on afterwards. Shown for a current savegame too, whose head
    /// can differ from the revision it was last played on.
    /// </summary>
    private string? DescribeRunsOn(Repo repo, SavegameDto savegame)
    {
        if (SavegameRevisionRules.TargetRevisionOf(savegame) is int pinned)
        {
            return $"This savegame stays on rev {pinned}. Playing it does not move it forward.";
        }

        return profileService.FindLive(repo.Id, savegame.ProfileId) is ProfileDto profile
            ? $"Will run on {profile.Name} rev {profile.HeadRevision}."
            : null;
    }

    /// <summary>
    /// Why the pre-selection is what it is, or null in the ordinary case, where the slot this save
    /// was last in is free.
    /// </summary>
    private static string? DescribeSuggestion(
        IReadOnlyList<SavegameSlotOptionViewModel> options,
        SavegameSlotRef? suggested,
        SavegameSlotRef? hint)
    {
        if (suggested is null)
        {
            return options.Count == 0
                ? "This game reports no savegame slots at all."
                : "Every slot has something in it, so there is nothing to pre-select. Pick the one to write over - anything ModsDude has a copy of can be put back.";
        }

        if (hint is not SavegameSlotRef remembered || remembered.Addresses(suggested.Value))
        {
            return null;
        }

        var taken = options.FirstOrDefault(x => x.Ref.Addresses(remembered));

        // Gone covers the whole folder having gone as well as the slot.
        return taken is null
            ? "The slot this save was last in is gone, so the first free one is picked instead."
            : $"The slot this save was last in now holds '{taken.Label}', so the first free one is picked instead.";
    }

    /// <summary>
    /// What the mod folder would have to do. Null where the adapter has no mods or the folder cannot be
    /// read.
    /// </summary>
    private async Task<SavegameModsSummary?> BuildModsSummaryAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false || profileService.FindLive(repo.Id, savegame.ProfileId) is not ProfileDto profile)
        {
            return null;
        }

        // The revision is named rather than resolved from the game, which holds nothing for this
        // savegame yet: the plan shown has to be the plan that runs. On the strip because planning
        // reads and hashes the mod folder while somebody waits for the modal.
        using var task = backgroundTasks.Begin($"Checking what '{savegame.Name}' would need");

        var plans = (await applyService.TryPlanAsync(
            repo,
            game,
            profile.Id,
            profile.Name,
            SavegameRevisionRules.TargetRevisionOf(savegame),
            cancellationToken,
            ProfileApplyService.Report(task, null))).Plans;

        if (plans.Count == 0)
        {
            return null;
        }

        // Summed across every folder the game reaches.
        if (plans.Any(x => x.HasWork) is false)
        {
            return new SavegameModsSummary(true, "Mods are already correct.", [], null);
        }

        var parts = new List<string>();
        var installs = plans.Sum(x => x.InstallCount);
        var replaces = plans.Sum(x => x.ReplaceCount);
        var uninstalls = plans.Sum(x => x.UninstallCount);
        var renames = plans.Sum(x => x.RenameCount);
        var unrecognised = plans.Sum(x => x.Unrecognised.Count);

        if (installs > 0) parts.Add($"{installs} to install");
        if (replaces > 0) parts.Add($"{replaces} to replace");
        if (uninstalls > 0) parts.Add($"{uninstalls} to uninstall");
        if (renames > 0) parts.Add($"{renames} to rename");

        // A rename leaves the bytes alone, so a locked mod being renamed is not worth warning about.
        var locked = plans
            .SelectMany(x => x.Items)
            .Where(x => x.Locked && x.Action is not (ModSyncAction.Keep or ModSyncAction.Rename))
            .Select(x => $"'{x.DisplayName}'")
            .Distinct()
            .ToList();

        return new SavegameModsSummary(
            false,
            string.Join(", ", parts) + $" · {plans.Sum(x => x.KeepCount)} already correct.",
            locked,
            unrecognised > 0
                ? $"{unrecognised} mods in the folder are not in the repo. You are asked about those separately, before anything moves."
                : null);
    }

    /// <summary>
    /// Which revision the save was last played on against the one the profile is now at. Null where
    /// they are the same.
    /// </summary>
    private async Task<SavegameRevisionNote?> BuildRevisionNoteAsync(Repo repo, SavegameDto savegame, CancellationToken cancellationToken)
    {
        if (savegame.Head is not SavegameSnapshotDto head ||
            head.ProfileRevision is not int played ||
            profileService.FindLive(repo.Id, savegame.ProfileId) is not ProfileDto profile ||
            profile.HeadRevision <= played)
        {
            return null;
        }

        var moved = await lockedPinDrift.HasMovedAsync(repo.Id, profile.Id, played, profile.HeadRevision, cancellationToken);

        var text = $"Last played on revision {played}; {profile.Name} is now at {profile.HeadRevision}.";

        return new SavegameRevisionNote(
            moved
                ? text + " A locked mod moved between them, and hosting this save on it may damage it."
                : text,
            moved);
    }
}
