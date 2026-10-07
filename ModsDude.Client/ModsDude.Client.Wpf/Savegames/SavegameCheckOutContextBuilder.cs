using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;

namespace ModsDude.Client.Wpf.Savegames;

public sealed class SavegameCheckOutContextBuilder(
    ISavegameSlots savegameSlots,
    ISavegameBindingStore bindingStore,
    IProfileStore profileStore,
    IProfileApplyService applyService,
    IBackgroundTaskReporter backgroundTasks) : ISavegameCheckOutContextBuilder
{
    public async Task<SavegameCheckOutContext> BuildAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        SavegameCheckOutMode mode,
        int? pinnedRevision,
        SavegameCompatibilityVerdict? verdict,
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
        IReadOnlySet<Guid> checkedInFirst,
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
                binding is SavegameCheckoutBinding held ? heldNames.GetValueOrDefault(held.SavegameId) ?? HeldSavegameName.Unknown : null,
                binding is SavegameCheckoutBinding freed && checkedInFirst.Contains(freed.SavegameId)));
        }

        var suggested = await savegameSlots.SuggestSlotAsync(game, savegame.Id, cancellationToken);
        var hint = bindingStore.GetSlotHint(game.Identity, savegame.Id);

        return new SavegameCheckOutContext(
            options,
            suggested,
            DescribeSuggestion(options, suggested, hint),
            mode is SavegameCheckOutMode.CheckOut
                ? await BuildModsSummaryAsync(repo, savegame, game, pinnedRevision, cancellationToken)
                : null,
            DescribeRevision(savegame, pinnedRevision, verdict),
            // Absent for a copy, which applies nothing.
            mode is SavegameCheckOutMode.CheckOut ? DescribeRunsOn(repo, savegame, pinnedRevision) : null);
    }

    /// <summary>
    /// Which revision the folder will be on afterwards. Shown on latest too, whose head can differ
    /// from the revision the save was last played on.
    /// </summary>
    private string? DescribeRunsOn(Repo repo, SavegameDto savegame, int? pinnedRevision)
    {
        if (pinnedRevision is int pinned)
        {
            return $"Compatibility mode: stays on rev {pinned} while checked out.";
        }

        return profileStore.Find(repo.Id, savegame.ProfileId) is Profile profile
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
        int? pinnedRevision,
        CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false || profileStore.Find(repo.Id, savegame.ProfileId) is not Profile profile)
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
            pinnedRevision,
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
        var unlinks = plans.Sum(x => x.UnlinkCount);
        var unrecognised = plans.Sum(x => x.Unrecognised.Count);

        if (installs > 0) parts.Add($"{installs} to install");
        if (replaces > 0) parts.Add($"{replaces} to replace");
        if (uninstalls > 0) parts.Add($"{uninstalls} to uninstall");
        if (renames > 0) parts.Add($"{renames} to rename");
        if (unlinks > 0) parts.Add($"{unlinks} to copy instead of link");

        // A rename or an unlink leaves the bytes alone, so a locked mod going through one is not worth warning about.
        var locked = plans
            .SelectMany(x => x.Items)
            .Where(x => x.Locked && x.Action is not (ModSyncAction.Keep or ModSyncAction.Rename or ModSyncAction.Unlink))
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
    /// they are the same, and in compatibility mode, where the folder stays on the played one.
    /// </summary>
    private static SavegameRevisionNote? DescribeRevision(
        SavegameDto savegame,
        int? pinnedRevision,
        SavegameCompatibilityVerdict? verdict)
    {
        if (pinnedRevision is not null || verdict is null)
        {
            return null;
        }

        return new SavegameRevisionNote(
            $"Last played on rev {verdict.Comparison.From}; {SavegameWording.ProfileOf(savegame)} is now at rev {verdict.Comparison.To}.",
            verdict.ShouldPrompt);
    }
}
