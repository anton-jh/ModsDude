using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.IO;

namespace ModsDude.Client.Wpf.ViewModel.Services;

public enum ProfileApplyStatus
{
    /// <summary>The folder now matches the profile.</summary>
    Applied,

    /// <summary>It already did, so nothing was touched.</summary>
    AlreadyMatched,

    /// <summary>The user backed out of the confirmation.</summary>
    Declined,

    /// <summary>
    /// A savegame checked out on the instance follows another mod list. Not a "not now" like
    /// <see cref="Unavailable"/> - nothing about waiting changes it, and the way out is to check that
    /// savegame in.
    /// </summary>
    Refused,

    /// <summary>
    /// A dedicated server mid-session, a folder held by a running game, an unplugged drive. Reported
    /// and left drifted - which is a "not now", and which the drift notice already covers.
    /// </summary>
    Unavailable,

    Failed
}

public sealed record ProfileApplyOutcome(LocalInstance Instance, ProfileApplyStatus Status, string Message)
{
    public bool Succeeded => Status is ProfileApplyStatus.Applied or ProfileApplyStatus.AlreadyMatched;

    /// <summary>
    /// Whether the instance should now be recorded as following this profile.
    /// </summary>
    /// <remarks>
    /// <b>True even where the folder could not be touched</b>, which is the long-standing rule: the
    /// instance is still meant to follow this profile and being left drifted is what the notice is
    /// for. False for the two answers that are not "not now" - the user backing out, and a savegame
    /// held here that refuses the switch outright. Recording the intent for that second one would
    /// leave an instance whose standing profile is one its own held savegame forbids applying.
    /// </remarks>
    public bool RecordsIntent => Status is not (ProfileApplyStatus.Declined or ProfileApplyStatus.Refused);

    /// <summary>
    /// The savegame whose hold refused this, so a caller that has the list can name it. Null for
    /// every other status.
    /// </summary>
    public Guid? BlockedBySavegameId { get; init; }
}


/// <summary>
/// Applying a profile to an instance from anywhere that is not the sync page: the drift notice's
/// one-click re-apply, the mod list editor's save, and the shell-level activation control.
/// </summary>
/// <remarks>
/// <para>
/// The sync page stays as it is - it exists to show the plan and let the user read it before
/// deciding. This is the other shape, where the decision has already been made and the plan is only
/// worth interrupting for when it would destroy something the repo cannot put back.
/// </para>
/// <para>
/// The modal host is taken lazily because it is the shell itself: the drift notice is built as part
/// of <c>MainWindowViewModel</c>, which is what <see cref="IModalService"/> resolves to, so asking
/// for it in the constructor closes a cycle the container never gets out of. Nothing here needs a
/// dialog until the user applies something, which is long after the shell exists.
/// </para>
/// </remarks>
public sealed class ProfileApplyService(
    ModSyncService syncService,
    IHeldSavegames heldSavegames,
    Lazy<IModalService> modalService,
    IBackgroundTaskReporter backgroundTasks)
{
    /// <summary>
    /// Works out what would change. Returns null where the instance cannot be applied to right now.
    /// </summary>
    /// <param name="revision">
    /// Which revision to plan against, or null to let the instance decide - a past savegame held
    /// there pins the folder to its own revision, and everything else follows head. Named only by the
    /// check-out dialog, which is previewing the apply for a savegame nothing is holding yet.
    /// </param>
    public async Task<ModSyncPlan?> TryPlanAsync(
        Repo repo,
        LocalInstance instance,
        Guid profileId,
        string? profileName,
        int? revision,
        CancellationToken cancellationToken)
    {
        if (GetAdapter(repo, instance) is not IInstanceModAdapter adapter)
        {
            return null;
        }

        try
        {
            return await syncService.PlanAsync(
                new ModSyncRequest(instance.Id, adapter, repo.Id, profileId) { ProfileName = profileName, Revision = revision },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is UserFriendlyException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Plans, confirms anything unrecoverable, and executes. One call, because the caller has already
    /// decided - the whole point of the drift notice's second action is that it costs one click.
    /// </summary>
    /// <param name="confirmPlan">
    /// Whether to show the plan before executing. Activation moves an instance onto a different
    /// profile, which uninstalls whatever the previous one put there; the reconciler already knows
    /// exactly what that is, so it is shown rather than a bare "are you sure". A re-apply of the
    /// profile the instance is already on has nothing to disclose beyond the destructive part, which
    /// is confirmed either way.
    /// </param>
    /// <param name="revision">
    /// Which revision to install, or null - nearly always - to let the instance decide, per
    /// <see cref="TryPlanAsync"/>. Named by the savegame list's <em>Apply profile</em>, which is
    /// preparing the folder for a savegame nothing is holding yet: a past one runs on its own revision,
    /// and letting the instance decide would install head and leave the check-out that follows
    /// immediately drifted.
    /// </param>
    public async Task<ProfileApplyOutcome> ApplyAsync(
        Repo repo,
        LocalInstance instance,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision = null)
    {
        // Asked before anything is planned, because this refusal is not about the folder and reading
        // it costs a list lookup. The sync engine refuses it too - that one is the backstop nothing
        // can get past; this one is the sentence somebody can act on.
        if (heldSavegames.DecideApply(instance.Id, profileId, revision) is { IsAllowed: false } refusal)
        {
            // Two refusals, two sentences. A past savegame held here is not following "another mod list" -
            // it is following this very one and does not move off its revision - and telling somebody
            // to check it in over a revision mismatch would be advice that fixes nothing.
            var reason = refusal.Refusal is SavegameApplyRefusal.PastSavegameIsHeld
                ? $"'{instance.Name}' is holding a past savegame, which runs on revision {refusal.Revision} and does not move off it. It was left as it is."
                : $"'{instance.Name}' is holding a savegame that follows another mod list, so it was left as it is. Check that savegame in first.";

            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Refused, reason)
            {
                BlockedBySavegameId = refusal.SavegameId
            };
        }

        ModSyncPlan? plan;

        try
        {
            plan = await TryPlanAsync(repo, instance, profileId, profileName, revision, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was stopped before anything changed.");
        }

        if (plan is null)
        {
            return new ProfileApplyOutcome(
                instance,
                ProfileApplyStatus.Unavailable,
                $"'{instance.Name}' could not be reached, so it was left as it is. It will keep showing as drifted until it can be.");
        }

        if (plan.HasWork is false)
        {
            // Nothing to change, but possibly plenty to record: a mod put in the folder by hand and
            // then imported and pinned makes the folder right and the manifest wrong, and drift is
            // measured against the manifest. Without this the notice reports an addition that
            // re-applying can never clear, while telling the user the folder already matches.
            await syncService.RecordAlreadyMatchedAsync(plan);

            return new ProfileApplyOutcome(
                instance, ProfileApplyStatus.AlreadyMatched, $"'{instance.Name}' already matches{Pinned(instance, profileId, revision)}.");
        }

        if (confirmPlan && await ConfirmPlanAsync(instance, plan) is false)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was left as it is.");
        }

        if (plan.Unrecognised.Count > 0 && await ConfirmUnrecognisedAsync(plan) is false)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was left as it is.");
        }

        // Only from here: everything above is planning and asking, which is quick or is a dialog the
        // user is already looking at. The strip is for the part that takes minutes and that they are
        // entitled to walk away from.
        using var task = backgroundTasks.Begin($"Applying '{profileName ?? "a profile"}' to '{instance.Name}'");

        try
        {
            var result = await syncService.ExecuteAsync(plan, Report(task, progress), cancellationToken);

            return result.Completed
                ? new ProfileApplyOutcome(instance, ProfileApplyStatus.Applied, $"'{instance.Name}' now matches{Pinned(instance, profileId, revision)}.")
                : new ProfileApplyOutcome(
                    instance,
                    ProfileApplyStatus.Failed,
                    $"'{instance.Name}': {result.Failures.Count} mods could not be applied.");
        }
        catch (OperationCanceledException)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was stopped part way.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProfileApplyOutcome(
                instance,
                ProfileApplyStatus.Unavailable,
                $"'{instance.Name}' is in use - a running game or a server mid-session holds its folder. It was left drifted.");
        }
    }

    /// <summary>
    /// The reconciler's own plan as the confirmation. It already computes exactly what would change,
    /// so showing it beats asking "are you sure" about something the user cannot see.
    /// </summary>
    public async Task<bool> ConfirmPlanAsync(LocalInstance instance, ModSyncPlan plan)
    {
        var lines = new List<string>();

        if (plan.InstallCount > 0) lines.Add($"{plan.InstallCount} to install");
        if (plan.ReplaceCount > 0) lines.Add($"{plan.ReplaceCount} to replace");
        if (plan.UninstallCount > 0) lines.Add($"{plan.UninstallCount} to uninstall");
        if (plan.QuarantineCount > 0) lines.Add($"{plan.QuarantineCount} to move to the Recycle Bin");
        if (plan.RenameCount > 0) lines.Add($"{plan.RenameCount} to rename");

        var modal = new ConfirmationDialogViewModel(
            $"Apply to '{instance.Name}'?",
            $"{plan.ModFolder}\n\n" +
            $"{string.Join('\n', lines)}\n" +
            $"{plan.KeepCount} already correct.\n\n" +
            "Anything the profile does not pin is taken out of the folder.",
            IconKind.Question,
            "Apply",
            "Cancel");

        await modalService.Value.Show(modal);

        return modal.Result;
    }

    /// <summary>
    /// The one interruption a re-apply is always worth: files nothing else on the machine has a copy
    /// of, named, with where they are going.
    /// </summary>
    public async Task<bool> ConfirmUnrecognisedAsync(ModSyncPlan plan)
    {
        var names = plan.Unrecognised.Take(10).Select(x => $"  {x.DisplayName}");
        var more = plan.Unrecognised.Count > 10 ? $"\n  ...and {plan.Unrecognised.Count - 10} more" : "";

        var modal = new ConfirmationDialogViewModel(
            "These are not in the repo",
            $"{plan.Unrecognised.Count} installed files are not registered in this repo, so nothing else has a copy of them:\n\n" +
            $"{string.Join('\n', names)}{more}\n\n" +
            "They will be moved to the Windows Recycle Bin, where you can restore them. Nothing is deleted.",
            IconKind.Warning,
            "Apply the profile",
            "Cancel");

        await modalService.Value.Show(modal);

        return modal.Result;
    }


    /// <summary>
    /// Feeds the shell strip and whatever the caller asked for from the one stream of reports.
    /// </summary>
    /// <remarks>
    /// A page's own progress and the shell's are not alternatives: the page draws a row per mod and
    /// the shell draws one line that survives navigating away from that page, and a sync started from
    /// the drift notice has no page at all. So this forwards rather than replacing, and the caller
    /// passing null is the ordinary case rather than the special one.
    /// </remarks>
    public static IProgress<ModSyncProgress> Report(IBackgroundTask task, IProgress<ModSyncProgress>? inner)
    {
        return new SyncProgressRelay(task, inner);
    }

    /// <summary>
    /// Which revision an apply ended on, said out loud only where it is not the profile's latest.
    /// </summary>
    /// <remarks>
    /// "Now matches" is a sentence about following the profile, and it stops being true on its own
    /// terms the moment the folder is put somewhere behind head. Saying the number is what keeps the
    /// ordinary case silent and the pinned one honest, without the caller having to know a savegame is
    /// involved. A caller that named a revision gets the plainer half of it: nothing is holding that
    /// savegame yet, so there is no checked-out save to explain the number by.
    /// </remarks>
    private string Pinned(LocalInstance instance, Guid profileId, int? revision)
    {
        if (revision is int named)
        {
            return $" revision {named}";
        }

        return heldSavegames.GetRequiredRevision(instance.Id, profileId) is int held
            ? $" revision {held}, which is what the savegame checked out there runs on"
            : "";
    }

    private static IInstanceModAdapter? GetAdapter(Repo repo, LocalInstance instance)
    {
        return instance.GetAdapter(repo.Adapter)
            .GetInstanceCapabilityAdapterFactory<IInstanceModAdapter>()
            ?.Invoke();
    }


    /// <summary>
    /// One sync report, said twice: once to the shell strip in a sentence, once onward to whatever
    /// the caller wanted it for.
    /// </summary>
    /// <remarks>
    /// The mod count is the proportion rather than the byte count: a sync is dozens of files of very
    /// different sizes, and a bar that jumped and stalled per file would be less informative than one
    /// that walks. Bytes are what the <em>page's</em> per-row bars are for.
    /// </remarks>
    private sealed class SyncProgressRelay(IBackgroundTask task, IProgress<ModSyncProgress>? inner)
        : IProgress<ModSyncProgress>
    {
        public void Report(ModSyncProgress value)
        {
            task.Report(Describe(value), value.Completed, value.Total);

            inner?.Report(value);
        }

        private static string Describe(ModSyncProgress value)
        {
            var what = value.Detail ?? value.ModId;

            return what is null
                ? value.Phase.ToString()
                : $"{value.Phase}: {what}";
        }
    }
}
