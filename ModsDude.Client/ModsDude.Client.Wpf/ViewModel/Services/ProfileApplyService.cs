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
    /// A savegame checked out on the game follows another mod list. Not a "not now" like
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

public sealed record ProfileApplyOutcome(Game Game, ProfileApplyStatus Status, string Message)
{
    public bool Succeeded => Status is ProfileApplyStatus.Applied or ProfileApplyStatus.AlreadyMatched;

    /// <summary>
    /// Whether the game should now be recorded as following this profile.
    /// </summary>
    /// <remarks>
    /// <b>True even where the folder could not be touched</b>, which is the long-standing rule: the
    /// game is still meant to follow this profile and being left drifted is what the notice is
    /// for. False for the two answers that are not "not now" - the user backing out, and a savegame
    /// held here that refuses the switch outright. Recording the intent for that second one would
    /// leave a game whose standing profile is one its own held savegame forbids applying.
    /// </remarks>
    public bool RecordsIntent => Status is not (ProfileApplyStatus.Declined or ProfileApplyStatus.Refused);

    /// <summary>
    /// The savegame whose hold refused this, so a caller that has the list can name it. Null for
    /// every other status.
    /// </summary>
    public Guid? BlockedBySavegameId { get; init; }
}


/// <summary>
/// Applying a profile to a game from anywhere that is not the sync page: the drift notice's
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
    /// Works out what would change, one plan per folder the game reaches.
    /// </summary>
    /// <remarks>
    /// <b>A list rather than a plan, because a game reaching three folders has three of them.</b>
    /// Sync's unit of work is genuinely one folder - see <see cref="ModSyncRequest"/> - so the loop
    /// lives here, at the thing that applies a profile to a <em>game</em>. Empty means there was
    /// nothing to plan: no mod capability, no folder configured, or none of them reachable right now.
    /// </remarks>
    /// <param name="revision">
    /// Which revision to plan against, or null to let the game decide - a past savegame held
    /// there pins the folder to its own revision, and everything else follows head. Named only by the
    /// check-out dialog, which is previewing the apply for a savegame nothing is holding yet.
    /// </param>
    public async Task<IReadOnlyList<ModSyncPlan>> TryPlanAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        int? revision,
        CancellationToken cancellationToken)
    {
        if (GetAdapter(repo, game) is not ILocalModAdapter adapter)
        {
            return [];
        }

        var plans = new List<ModSyncPlan>();

        foreach (var target in adapter.ModTargets)
        {
            // Per folder, and one that cannot be planned does not cost the others theirs: a
            // dedicated server mid-session is exactly the folder somebody wants left out while the
            // client is put right.
            if (await TryPlanTargetAsync(adapter, game, target, repo.Id, profileId, profileName, revision, cancellationToken)
                is ModSyncPlan plan)
            {
                plans.Add(plan);
            }
        }

        return plans;
    }

    /// <summary>One folder's plan, or null where that folder cannot be planned against right now.</summary>
    private async Task<ModSyncPlan?> TryPlanTargetAsync(
        ILocalModAdapter adapter,
        Game game,
        ModTarget target,
        Guid repoId,
        Guid profileId,
        string? profileName,
        int? revision,
        CancellationToken cancellationToken)
    {
        try
        {
            return await syncService.PlanAsync(
                new ModSyncRequest(game.Identity, target, adapter, repoId, profileId)
                {
                    ProfileName = profileName,
                    Revision = revision
                },
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
    /// Whether to show the plan before executing. Activation moves a game onto a different
    /// profile, which uninstalls whatever the previous one put there; the reconciler already knows
    /// exactly what that is, so it is shown rather than a bare "are you sure". A re-apply of the
    /// profile the game is already on has nothing to disclose beyond the destructive part, which
    /// is confirmed either way.
    /// </param>
    /// <param name="revision">
    /// Which revision to install, or null - nearly always - to let the game decide, per
    /// <see cref="TryPlanAsync"/>. Named by the savegame list's <em>Apply profile</em>, which is
    /// preparing the folder for a savegame nothing is holding yet: a past one runs on its own revision,
    /// and letting the game decide would install head and leave the check-out that follows
    /// immediately drifted.
    /// </param>
    public async Task<ProfileApplyOutcome> ApplyAsync(
        Repo repo,
        Game game,
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
        if (heldSavegames.DecideApply(game.Identity, profileId, revision) is { IsAllowed: false } refusal)
        {
            // Two refusals, two sentences. A past savegame held here is not following "another mod list" -
            // it is following this very one and does not move off its revision - and telling somebody
            // to check it in over a revision mismatch would be advice that fixes nothing.
            var reason = refusal.Refusal is SavegameApplyRefusal.PastSavegameIsHeld
                ? $"'{game.Name}' is holding a past savegame, which runs on revision {refusal.Revision} and does not move off it. It was left as it is."
                : $"'{game.Name}' is holding a savegame that follows another mod list, so it was left as it is. Check that savegame in first.";

            return new ProfileApplyOutcome(game, ProfileApplyStatus.Refused, reason)
            {
                BlockedBySavegameId = refusal.SavegameId
            };
        }

        IReadOnlyList<ModSyncPlan> plans;

        try
        {
            plans = await TryPlanAsync(repo, game, profileId, profileName, revision, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Declined, $"'{game.Name}' was stopped before anything changed.");
        }

        if (plans.Count == 0)
        {
            return new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Unavailable,
                $"'{game.Name}' could not be reached, so it was left as it is. It will keep showing as drifted until it can be.");
        }

        // One folder at a time, each with its own answer. A failure here is per folder by design -
        // the dedicated server being locked mid-session must not stop the client being put right -
        // and the folded answer below is what a caller that holds a game rather than a folder reads.
        var outcomes = new List<ProfileApplyOutcome>();

        foreach (var plan in plans)
        {
            outcomes.Add(await ApplyTargetAsync(plan, game, profileId, profileName, confirmPlan, progress, cancellationToken, revision));
        }

        return Combine(game, outcomes);
    }

    /// <summary>
    /// One answer for a game whose folders were applied to one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The status is the one that most needs saying</b>, and every sentence is kept: a game that
    /// applied to its server folder and could not reach its client folder says both, because the
    /// half that worked is not the news. <see cref="ProfileApplyStatus.Declined"/> ranks last on
    /// purpose - it only speaks for the game where <em>every</em> folder was declined, since
    /// <see cref="ProfileApplyOutcome.RecordsIntent"/> hangs off this and a game with one folder
    /// already put right plainly means to follow the profile.
    /// </para>
    /// <para>
    /// Scaffolding, and the shape slice 4 of Phase 10 replaces: activation becomes one gesture with
    /// one confirmation across every folder, which is what makes a partly-declined activation
    /// unrepresentable rather than something to fold. <see cref="ProfileApplyStatus.Refused"/> is
    /// never folded - it is decided once for the game, before the loop.
    /// </para>
    /// </remarks>
    private static ProfileApplyOutcome Combine(Game game, IReadOnlyList<ProfileApplyOutcome> perTarget)
    {
        if (perTarget.Count == 1)
        {
            return perTarget[0];
        }

        var worst = perTarget
            .OrderBy(x => x.Status switch
            {
                ProfileApplyStatus.Failed => 0,
                ProfileApplyStatus.Unavailable => 1,
                ProfileApplyStatus.Applied => 2,
                ProfileApplyStatus.AlreadyMatched => 3,
                _ => 4
            })
            .First();

        return worst with
        {
            Game = game,
            Message = string.Join(' ', perTarget.Select(x => x.Message).Distinct())
        };
    }

    /// <summary>One folder: confirm what it would destroy, then make it match.</summary>
    private async Task<ProfileApplyOutcome> ApplyTargetAsync(
        ModSyncPlan plan,
        Game game,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision)
    {
        var where = Where(game, plan);

        if (plan.HasWork is false)
        {
            // Nothing to change, but possibly plenty to record: a mod put in the folder by hand and
            // then imported and pinned makes the folder right and the manifest wrong, and drift is
            // measured against the manifest. Without this the notice reports an addition that
            // re-applying can never clear, while telling the user the folder already matches.
            await syncService.RecordAlreadyMatchedAsync(plan);

            return new ProfileApplyOutcome(
                game, ProfileApplyStatus.AlreadyMatched, $"{where} already matches{Pinned(game, profileId, revision)}.");
        }

        if (confirmPlan && await ConfirmPlanAsync(game, plan) is false)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Declined, $"{where} was left as it is.");
        }

        if (plan.Unrecognised.Count > 0 && await ConfirmUnrecognisedAsync(plan) is false)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Declined, $"{where} was left as it is.");
        }

        // Only from here: everything above is planning and asking, which is quick or is a dialog the
        // user is already looking at. The strip is for the part that takes minutes and that they are
        // entitled to walk away from.
        using var task = backgroundTasks.Begin($"Applying '{profileName ?? "a profile"}' to {where}");

        try
        {
            var result = await syncService.ExecuteAsync(plan, Report(task, progress), cancellationToken);

            return result.Completed
                ? new ProfileApplyOutcome(game, ProfileApplyStatus.Applied, $"{where} now matches{Pinned(game, profileId, revision)}.")
                : new ProfileApplyOutcome(
                    game,
                    ProfileApplyStatus.Failed,
                    $"{where}: {result.Failures.Count} mods could not be applied.");
        }
        catch (OperationCanceledException)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Declined, $"{where} was stopped part way.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Unavailable,
                $"{where} is in use - a running game or a server mid-session holds its folder. It was left drifted.");
        }
    }

    /// <summary>
    /// What to call the thing a sentence is about: the game, or one of its folders where it has more
    /// than one to tell apart.
    /// </summary>
    /// <remarks>
    /// A game with one target names nothing, so every sentence here reads exactly as it did before
    /// targets existed - and <see cref="ProfileApplyOutcome.Combine"/> then folds three identical
    /// sentences into one. A game with three names the folder, because "2 mods could not be applied"
    /// without saying <em>where</em> is the complaint this phase exists to answer.
    /// </remarks>
    private static string Where(Game game, ModSyncPlan plan)
    {
        return plan.Target.DisplayName is string folder
            ? $"'{game.Name}' ({folder})"
            : $"'{game.Name}'";
    }

    /// <summary>
    /// The reconciler's own plan as the confirmation. It already computes exactly what would change,
    /// so showing it beats asking "are you sure" about something the user cannot see.
    /// </summary>
    public async Task<bool> ConfirmPlanAsync(Game game, ModSyncPlan plan)
    {
        var lines = new List<string>();

        if (plan.InstallCount > 0) lines.Add($"{plan.InstallCount} to install");
        if (plan.ReplaceCount > 0) lines.Add($"{plan.ReplaceCount} to replace");
        if (plan.UninstallCount > 0) lines.Add($"{plan.UninstallCount} to uninstall");
        if (plan.QuarantineCount > 0) lines.Add($"{plan.QuarantineCount} to move to the Recycle Bin");
        if (plan.RenameCount > 0) lines.Add($"{plan.RenameCount} to rename");

        var modal = new ConfirmationDialogViewModel(
            $"Apply to '{game.Name}'?",
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
    private string Pinned(Game game, Guid profileId, int? revision)
    {
        if (revision is int named)
        {
            return $" revision {named}";
        }

        return heldSavegames.GetRequiredRevision(game.Identity, profileId) is int held
            ? $" revision {held}, which is what the savegame checked out there runs on"
            : "";
    }

    private static ILocalModAdapter? GetAdapter(Repo repo, Game game)
    {
        return game.GetAdapter(repo.Adapter)
            .GetLocalCapabilityAdapterFactory<ILocalModAdapter>()
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
