using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.Mods.Import;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.IO;

namespace ModsDude.Client.Wpf.Profiles;

public enum ProfileApplyStatus
{
    /// <summary>The folder now matches the profile.</summary>
    Applied,

    /// <summary>It already did, so nothing was touched.</summary>
    AlreadyMatched,

    /// <summary>The user backed out of the confirmation.</summary>
    Declined,

    /// <summary>
    /// Cancelled while planning or part way through the work. Not <see cref="Declined"/>: nothing was
    /// said about the plan itself, so a caller that reads a decline as "not with these files" - and
    /// offers a way round them - has no reason to here.
    /// </summary>
    Stopped,

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

    /// <summary>
    /// Something else is already applying to one of these folders.
    /// </summary>
    /// <remarks>
    /// A "not now" like <see cref="Unavailable"/>, but about this app rather than about the machine,
    /// and therefore one that clears itself: the message names what is running, and the buttons that
    /// lead here are greyed while it is. Reaching this at all means a race - the notice clicked in the
    /// same second as the button - because every entry point asks the lease before offering the gesture.
    /// </remarks>
    Busy,

    Failed,

    /// <summary>
    /// The game no longer follows a profile. The mod folders were either left as they were or, where
    /// the user asked for it, cleared - the message says which.
    /// </summary>
    Deactivated
}

public sealed record ProfileApplyOutcome(Game Game, ProfileApplyStatus Status, string Message)
{
    public bool Succeeded => Status is ProfileApplyStatus.Applied or ProfileApplyStatus.AlreadyMatched or ProfileApplyStatus.Deactivated;

    /// <summary>
    /// How loudly to say <see cref="Message"/>. Declining and stopping are the user's own answers, so
    /// they are worded like a success; everything else left the folder as it was or only part done.
    /// </summary>
    public ToastSeverity ToastSeverity => Succeeded || Status is ProfileApplyStatus.Declined or ProfileApplyStatus.Stopped
        ? ToastSeverity.Info
        : ToastSeverity.Warning;

    /// <summary>
    /// Whether this game now follows the profile - <b>what happened, not a rule to apply</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The intent is recorded by <see cref="ProfileApplyService.ActivateAsync"/> itself, before a
    /// single file moves, so nothing downstream has to know when it is right to record one. This is
    /// here for the callers that have their own bookkeeping to do about it - a page refreshing its
    /// activation label, an editor recomputing which games its save applies to.
    /// </para>
    /// <para>
    /// False for a pure apply, which never records anything, and for the two answers that stop an
    /// activation happening at all: a held savegame refusing it, and the user declining the plan.
    /// <b>True even where the folder could not be touched</b> - the game is still meant to follow
    /// this profile, and being left drifted is what the notice is for.
    /// </para>
    /// </remarks>
    public bool Activated { get; init; }

    /// <summary>
    /// The savegame whose hold refused this, so a caller that has the list can name it. Null for
    /// every other status.
    /// </summary>
    public Guid? BlockedBySavegameId { get; init; }
}


/// <summary>What planning an apply produced: a plan per folder that could be planned, and why any other could not.</summary>
public sealed record PlanAttempt(IReadOnlyList<ModSyncPlan> Plans, IReadOnlyList<string> Refusals)
{
    public static PlanAttempt None { get; } = new([], []);

    /// <summary>Why nothing could be planned, for a sentence; a folder that could not be reached where nothing says more.</summary>
    public string Describe() => Refusals.Count > 0
        ? string.Join(" ", Refusals)
        : "Its mod folders could not be reached.";
}


/// <summary>
/// The two verbs, for everywhere that is not the sync page: the drift notice's one-click re-apply,
/// the mod list editor's save, and the activation controls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Activating is intent; applying is work.</b>
/// <see cref="ActivateAsync"/> records which profile a game follows and then applies it, as one
/// gesture; <see cref="ApplyAsync"/> only does the work, because whatever it is applying is already
/// the game's standing intent. Activating implies applying and applying never implies activating.
/// </para>
/// <para>
/// <b>The split is structural, which is the whole of what makes a failure safe.</b> Everything that
/// can stop an activation from happening at all - a held savegame refusing it, the user declining
/// the plan - is answered before the intent is recorded, and everything after it is work. So a
/// failure leaves the game still meaning to follow the profile, with the folder that did not get
/// there reported as drifted, rather than quietly retracting a decision the user made.
/// </para>
/// <para>
/// <b>One confirmation per gesture, across every folder.</b> A game reaching three of them is one
/// decision about one profile; asking three times would let somebody accept the client's plan and
/// decline the server's, which is an activation half-consented-to and a state nothing downstream
/// could describe.
/// </para>
/// <para>
/// The sync page stays as it is - it exists to show the plan and let the user read it before
/// deciding. This is the other shape, where the decision has already been made and the plan is only
/// worth interrupting for when it would destroy something the repo cannot put back.
/// </para>
/// <para>
/// The modal host is taken lazily because it is the shell itself: the drift notice is built as part
/// of <c>MainWindowViewModel</c>, which is what <see cref="IModalService"/> resolves to, so asking
/// for it in the constructor closes a cycle the container never gets out of. Nothing here needs a
/// modal until the user applies something, which is long after the shell exists.
/// </para>
/// </remarks>
public sealed class ProfileApplyService(
    IModSyncService syncService,
    IGameRepository games,
    IHeldSavegames heldSavegames,
    Lazy<IModalService> modalService,
    IFilePickerService filePicker,
    IBackgroundTaskReporter backgroundTasks,
    IResourceLeases leases,
    IGameActivityReporter activity) : IProfileApplyService
{
    /// <summary>
    /// The folder the user last chose to keep unrecognised files in, so the second apply of a session
    /// starts where the first ended. Not persisted: it is a convenience for one sitting, and a folder
    /// somebody picked last week is not a standing instruction.
    /// </summary>
    private string? _lastQuarantineFolder;

    public async Task<PlanAttempt> TryPlanAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        int? revision,
        CancellationToken cancellationToken,
        IProgress<ModSyncProgress>? progress = null,
        bool clearAll = false)
    {
        // First, so that the manifest this apply writes names the same folder the drift check will
        // go on to look in.
        games.RefreshTargets(game, repo.Adapter);

        if (GetAdapter(repo, game) is not ILocalModAdapter adapter)
        {
            return PlanAttempt.None;
        }

        var plans = new List<ModSyncPlan>();
        var refusals = new List<string>();

        foreach (var target in adapter.ModTargets)
        {
            // Per folder, and one that cannot be planned does not cost the others theirs: a
            // dedicated server mid-session is exactly the folder somebody wants left out while the
            // client is put right.
            try
            {
                plans.Add(await syncService.PlanAsync(
                    new ModSyncRequest(game.Identity, game.Name, target, adapter, repo.Id, clearAll ? Guid.Empty : profileId)
                    {
                        ProfileName = clearAll ? null : profileName,
                        Revision = clearAll ? null : revision,
                        ClearAll = clearAll
                    },
                    cancellationToken,
                    progress));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is UserFriendlyException or IOException or UnauthorizedAccessException)
            {
                refusals.Add(exception is UserFriendlyException friendly ? friendly.DeveloperMessage : exception.Message);
            }
        }

        return new PlanAttempt(plans, refusals);
    }

    public Task<ProfileApplyOutcome> ActivateAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision = null,
        bool pinRevision = false,
        Guid? checkedOutSavegame = null)
        => RunAsync(
            repo, game, profileId, profileName, confirmPlan, progress, cancellationToken, revision, activate: true,
            pinned: pinRevision ? revision : null,
            checkedOutSavegame);

    public Task<ProfileApplyOutcome> ApplyAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision = null)
        => RunAsync(repo, game, profileId, profileName, confirmPlan, progress, cancellationToken, revision, activate: false, pinned: null, checkedOutSavegame: null);

    public async Task<ProfileApplyOutcome> DeactivateAsync(
        Repo repo,
        Game game,
        bool clearMods,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var lease = leases.TryAcquireExclusive(
            TargetRefs(repo, game).Select(ResourceKeys.Target),
            clearMods ? $"Clearing the mods from '{game.Name}'" : $"Deactivating '{game.Name}'");

        if (lease is null)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Busy, Busy(repo, game));
        }

        if (heldSavegames.FindProfileHold(game.Identity) is SavegameCheckoutBinding held)
        {
            return new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Refused,
                $"'{game.Name}' is holding a savegame that follows a mod list, so it was left as it is. Check that savegame in first.")
            {
                BlockedBySavegameId = held.SavegameId
            };
        }

        if (clearMods is false)
        {
            games.SetActiveProfile(game, null);
            activity.ReportCleared(game.Identity);

            return new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Deactivated,
                $"'{game.Name}' no longer follows a profile. Its mod folders were left exactly as they are.");
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        PlanAttempt attempt;

        try
        {
            using var planning = backgroundTasks.Begin(
                $"Working out what would be cleared from '{game.Name}'", cancel: stop.Cancel);

            attempt = await TryPlanAsync(
                repo, game, Guid.Empty, null, null, stop.Token, Report(planning, progress), clearAll: true);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Stopped, $"'{game.Name}' was stopped before anything changed.");
        }

        IReadOnlyList<ModSyncPlan> plans = attempt.Plans;

        if (plans.Count == 0)
        {
            // Nothing to plan against, so nothing to ask about - but the decision to stop following a
            // profile was made, and stands. The folders simply could not be reached to be cleared.
            games.SetActiveProfile(game, null);
            activity.ReportCleared(game.Identity);

            return new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Deactivated,
                $"'{game.Name}' no longer follows a profile, but its mod folders could not be cleared. {attempt.Describe()}");
        }

        var consent = await ConsentedAsync(game, plans, confirmPlan: true, clearing: true);

        if (consent.Given is false)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Declined, $"'{game.Name}' was left as it is.");
        }

        if (consent.QuarantineFolder is string keepIn)
        {
            plans = [.. plans.Select(x => x with { QuarantineFolder = keepIn })];
        }

        games.SetActiveProfile(game, null);
        activity.ReportCleared(game.Identity);

        var outcomes = new List<ProfileApplyOutcome>(attempt.Refusals.Select(x => new ProfileApplyOutcome(game, ProfileApplyStatus.Unavailable, x)));

        foreach (var plan in plans)
        {
            outcomes.Add(await ApplyTargetAsync(plan, game, Guid.Empty, null, progress, stop, revision: null, clearing: true));
        }

        var combined = Combine(game, outcomes);

        return combined.Succeeded
            ? combined with
            {
                Status = ProfileApplyStatus.Deactivated,
                Message = $"{combined.Message} '{game.Name}' no longer follows a profile."
            }
            : combined;
    }

    /// <summary>
    /// Both verbs, in the order the split defines: claim, refuse, plan, ask, <em>then</em> record,
    /// then work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gesture's folders are claimed here, before anything else, and held to the end.</b> This
    /// is the one chokepoint every route into an apply comes through - the profile page, the mod list
    /// editor's save, its activation offer, two places in the savegame list, and the drift notice,
    /// which is reachable from every view and belongs to no page at all - so it is the only place a
    /// claim cannot be forgotten by a route added later.
    /// </para>
    /// <para>
    /// <b>Held across the confirmation, deliberately.</b> A modal asking whether to take the previous
    /// profile's mods back out is part of this gesture, and another apply starting on that folder while
    /// the question is on screen is exactly what wants preventing - the answer would be about a plan
    /// that no longer describes the folder.
    /// </para>
    /// <para>
    /// <b>Refused rather than queued</b>, because a person is holding the mouse. See
    /// <see cref="ProfileApplyStatus.Busy"/>.
    /// </para>
    /// </remarks>
    private async Task<ProfileApplyOutcome> RunAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision,
        bool activate,
        int? pinned,
        Guid? checkedOutSavegame)
    {
        using var lease = leases.TryAcquireExclusive(
            TargetRefs(repo, game).Select(ResourceKeys.Target),
            $"{(activate ? "Activating" : "Applying")} '{profileName ?? "a profile"}' on '{game.Name}'");

        if (lease is null)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Busy, Busy(repo, game));
        }

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

        // Joined with whatever the caller passed, so the strip's Cancel and a page's own Cancel are the
        // same act - and so a gesture whose page has since been navigated away from is still
        // stoppable, which is the whole reason the strip outlives the page.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        PlanAttempt attempt;

        try
        {
            // On the strip from the first click, because this is not the quick half it was taken for:
            // a folder whose files no longer match the manifest is read and hashed in full here, which
            // on a real mod folder is minutes of a still window with the confirmation appearing at the
            // end of it. Its own task rather than the execute one below - planning may end in a
            // modal the user declines, and a strip entry that outlived that would describe work
            // nobody agreed to.
            using var planning = backgroundTasks.Begin(
                $"Working out what would change in '{game.Name}'", cancel: stop.Cancel);

            attempt = await TryPlanAsync(
                repo, game, profileId, profileName, revision, stop.Token, Report(planning, progress));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Stopped, $"'{game.Name}' was stopped before anything changed.");
        }

        IReadOnlyList<ModSyncPlan> plans = attempt.Plans;

        if (plans.Count == 0)
        {
            // Nothing to plan against and nothing to ask about - but an activation still happened:
            // the game means to follow this profile and the notice says so until a folder can be
            // reached. This is the whole of "a failed apply does not retract the activation",
            // reached before any work was possible at all.
            return Record(activate, repo, game, profileId, pinned, checkedOutSavegame, new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Unavailable,
                $"'{game.Name}' was left as it is, and will keep showing as drifted until it can be applied. {attempt.Describe()}"));
        }

        // Once, across every folder. Declining is one answer about one gesture, which is what stops
        // an activation being half-consented-to.
        var consent = await ConsentedAsync(game, plans, confirmPlan);

        if (consent.Given is false)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Declined, $"'{game.Name}' was left as it is.");
        }

        // Where the unrecognised files go is part of the answer, and the plans were made before it was
        // asked - so it is put on them here, for every folder, rather than asked of each.
        if (consent.QuarantineFolder is string keepIn)
        {
            plans = [.. plans.Select(x => x with { QuarantineFolder = keepIn })];
        }

        // Before the work, and only after every way of saying no has been offered. Everything below
        // is work, and work failing leaves this standing.
        RecordIntent(activate, repo, game, profileId, pinned, checkedOutSavegame);

        // One folder at a time, each with its own answer. A failure here is per folder by design -
        // the dedicated server being locked mid-session must not stop the client being put right -
        // and the folded answer below is what a caller that holds a game rather than a folder reads.
        var outcomes = new List<ProfileApplyOutcome>(attempt.Refusals.Select(x => new ProfileApplyOutcome(game, ProfileApplyStatus.Unavailable, x)));

        foreach (var plan in plans)
        {
            outcomes.Add(await ApplyTargetAsync(plan, game, profileId, profileName, progress, stop, revision));
        }

        return Combine(game, outcomes) with { Activated = activate };
    }

    /// <summary>
    /// The one confirmation, covering every folder the gesture touches.
    /// </summary>
    /// <remarks>
    /// Two questions rather than one, as before: what the apply would change, and - separately, and
    /// always - the files nothing else on the machine has a copy of. The second is asked even where
    /// the caller waived the first, because it is the only interruption a re-apply is ever worth.
    /// </remarks>
    private async Task<Consent> ConsentedAsync(Game game, IReadOnlyList<ModSyncPlan> plans, bool confirmPlan, bool clearing = false)
    {
        var work = plans.Where(x => x.HasWork).ToList();

        if (work.Count == 0)
        {
            return Consent.Yes;
        }

        if (confirmPlan && await ConfirmPlanAsync(game, work, clearing) is false)
        {
            return Consent.No;
        }

        if (work.Any(x => x.Unrecognised.Count > 0) is false)
        {
            return Consent.Yes;
        }

        return await ConfirmUnrecognisedAsync(work) is UnrecognisedFilesChoice choice
            ? new Consent(true, choice.Folder)
            : Consent.No;
    }

    /// <param name="Given">Whether the user agreed to go ahead.</param>
    /// <param name="QuarantineFolder">Where they asked for unrecognised files to go instead of the Recycle Bin, if they did.</param>
    private sealed record Consent(bool Given, string? QuarantineFolder)
    {
        public static Consent Yes { get; } = new(true, null);
        public static Consent No { get; } = new(false, null);
    }

    /// <summary>
    /// Writes down which profile this game follows, where the gesture was an activation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Skipped where it would write what is already there: <c>SetActiveProfile</c> saves the whole of
    /// local state and wakes the drift check, and re-applying the profile a game already follows is
    /// the commonest gesture in the app. The pin is part of the intent, so an activation that only
    /// moves the game between head and a pinned revision of the same profile is still a write.
    /// </para>
    /// <para>
    /// <b>Friends are told here too</b>, at the same moment and for the same reason: this is where the
    /// decision stands, whatever the folders go on to do. Every gesture is reported, a re-apply
    /// included - the lists sort on who has been playing lately - and only a change of profile or a
    /// check-out is reported as one.
    /// </para>
    /// </remarks>
    private void RecordIntent(bool activate, Repo repo, Game game, Guid profileId, int? pinned, Guid? checkedOutSavegame)
    {
        var intent = new ActiveProfile(repo.Id, profileId);
        var changed = activate && (game.ActiveProfile != intent || game.PinnedRevision != pinned);

        if (changed)
        {
            games.SetActiveProfile(game, intent, pinned);
        }

        var kind = checkedOutSavegame is not null ? GameActivityKind.SavegameCheckedOut
            : changed ? GameActivityKind.Activated
            : GameActivityKind.Reapplied;

        // What the game is held on now, never the revision the gesture happened to name: the savegame
        // list names head's number to prepare for a current savegame, and reporting that would pin
        // anybody following to a revision nothing holds.
        activity.Report(
            game.Identity,
            repo.Id,
            profileId,
            heldSavegames.GetRequiredRevision(game.Identity, profileId),
            kind,
            checkedOutSavegame);
    }

    /// <inheritdoc cref="RecordIntent"/>
    private ProfileApplyOutcome Record(bool activate, Repo repo, Game game, Guid profileId, int? pinned, Guid? checkedOutSavegame, ProfileApplyOutcome outcome)
    {
        RecordIntent(activate, repo, game, profileId, pinned, checkedOutSavegame);

        return outcome with { Activated = activate };
    }

    /// <summary>
    /// One answer for a game whose folders were applied to one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The status is the one that most needs saying</b>, and every sentence is kept: a game that
    /// applied to its server folder and could not reach its client folder says both, because the
    /// half that worked is not the news.
    /// </para>
    /// <para>
    /// <b>Three statuses never reach here.</b> The lease is claimed for the whole gesture before
    /// anything is planned, a savegame's refusal is decided once for the game, and declining is one
    /// answer to one confirmation covering every folder - so a fold never sees
    /// <see cref="ProfileApplyStatus.Declined"/>. What it can see is
    /// <see cref="ProfileApplyStatus.Stopped"/>, a cancellation part way through the work, which is
    /// genuinely per folder. A partly-declined activation stopped being representable when the
    /// confirmation moved, and a partly-busy one when the claim did.
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

    /// <summary>
    /// One folder, already consented to: make it match.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is asked in here.</b> The confirmation is one question about the whole gesture and
    /// is answered before this runs - see <see cref="ConsentedAsync"/> - so what is left per folder
    /// is the work and the sentence describing how it went.
    /// </remarks>
    /// <param name="stop">
    /// The gesture's cancellation, handed in whole rather than as a token so this can offer it to the
    /// strip. One source across every folder: stopping an apply is a decision about the gesture, and
    /// a Cancel that only abandoned the folder currently being written would leave the rest to run.
    /// </param>
    private async Task<ProfileApplyOutcome> ApplyTargetAsync(
        ModSyncPlan plan,
        Game game,
        Guid profileId,
        string? profileName,
        IProgress<ModSyncProgress>? progress,
        CancellationTokenSource stop,
        int? revision,
        bool clearing = false)
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
                game,
                ProfileApplyStatus.AlreadyMatched,
                clearing ? $"{where} has no mods to clear." : $"{where} already matches{Pinned(game, profileId, revision)}.");
        }

        // The second of the gesture's two strip entries. Planning had its own - see RunAsync - because
        // it is minutes of work in its own right; this one is the part that moves files, and both are
        // things the user is entitled to walk away from.
        using var task = backgroundTasks.Begin(
            clearing ? $"Clearing the mods from {where}" : $"Applying '{profileName ?? "a profile"}' to {where}",
            cancel: stop.Cancel);

        task.DeclareTransfers(TransferDirection.Download);

        try
        {
            var result = await syncService.ExecuteAsync(plan, Report(task, progress), stop.Token);

            return result.Completed
                ? new ProfileApplyOutcome(
                    game,
                    ProfileApplyStatus.Applied,
                    clearing ? $"{where} has had its mods cleared." : $"{where} now matches{Pinned(game, profileId, revision)}.")
                : new ProfileApplyOutcome(
                    game,
                    ProfileApplyStatus.Failed,
                    $"{where}: {result.Failures.Count} changes could not be applied.");
        }
        catch (OperationCanceledException)
        {
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Stopped, $"{where} was stopped part way.");
        }
        catch (GameRunningException exception)
        {
            // Started between the confirmation and the work. Nothing was touched.
            return new ProfileApplyOutcome(game, ProfileApplyStatus.Unavailable, exception.UserMessage);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProfileApplyOutcome(
                game,
                ProfileApplyStatus.Unavailable,
                $"{where} is in use - a running game or a server mid-session holds its folder. "
                    + (clearing ? "It was not fully cleared." : "It was left drifted."));
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

    public async Task<bool> ConfirmPlanAsync(Game game, IReadOnlyList<ModSyncPlan> plans, bool clearing = false)
    {
        var modal = clearing
            ? new ConfirmationModalViewModel(
                $"Clear the mods from '{game.Name}'?",
                string.Join("\n\n", plans.Select(Describe))
                    + $"\n\n'{game.Name}' will no longer follow a profile, and every mod in the folders above is taken out.",
                IconKind.Question,
                "Clear mods",
                "Cancel")
            : new ConfirmationModalViewModel(
                $"Apply to '{game.Name}'?",
                $"{DescribeDownloads(PlannedDownloads.Across(plans))}\n\n"
                    + string.Join("\n\n", plans.Select(Describe))
                    + "\n\nAnything the profile does not pin is taken out of the folder.",
                IconKind.Question,
                "Apply",
                "Cancel");

        await modalService.Value.Show(modal);

        return modal.Result;
    }

    /// <summary>
    /// What the apply will download, first in the modal because it is the part that costs something:
    /// time and, on a metered line, money. Says so when there is nothing to fetch, rather than saying
    /// nothing - "nothing" reads the same as "not worked out".
    /// </summary>
    public static string DescribeDownloads(PlannedDownloads downloads)
    {
        if (downloads.IsAny is false)
        {
            return "Nothing to download - every mod it needs is already on this machine.";
        }

        var mods = downloads.Count == 1 ? "1 mod" : $"{downloads.Count} mods";

        return $"{mods} to download, {ByteSize.Describe(downloads.Bytes)} in total.";
    }

    /// <summary>One folder's block of the plan modal: where it is, and what would happen there.</summary>
    private static string Describe(ModSyncPlan plan)
    {
        var lines = new List<string>();

        if (plan.InstallCount > 0) lines.Add($"{plan.InstallCount} to install");
        if (plan.ReplaceCount > 0) lines.Add($"{plan.ReplaceCount} to replace");
        if (plan.UninstallCount > 0) lines.Add($"{plan.UninstallCount} to uninstall");
        if (plan.QuarantineCount > 0) lines.Add($"{plan.QuarantineCount} to move to the Recycle Bin or a folder you choose");
        if (plan.RenameCount > 0) lines.Add($"{plan.RenameCount} to rename");

        return $"{plan.ModFolder}\n\n{string.Join('\n', lines)}\n{plan.KeepCount} already correct.";
    }

    public async Task<UnrecognisedFilesChoice?> ConfirmUnrecognisedAsync(IReadOnlyList<ModSyncPlan> plans)
    {
        var unrecognised = plans.SelectMany(x => x.Unrecognised).ToList();

        var modal = new UnrecognisedFilesModalViewModel(
            [.. unrecognised.Select(x => $"  {x.DisplayName}")],
            filePicker,
            _lastQuarantineFolder);

        await modalService.Value.Show(modal);

        if (modal.Result is not UnrecognisedFilesChoice choice)
        {
            return null;
        }

        // Only a folder is remembered: choosing the bin is the default and needs no memory, and one
        // apply going to the bin should not forget where the last one was sent.
        _lastQuarantineFolder = choice.Folder ?? _lastQuarantineFolder;

        return choice;
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

        if (heldSavegames.GetRequiredRevision(game.Identity, profileId) is not int held)
        {
            return "";
        }

        // The game's own pin only counts where no savegame is doing the pinning - the one that is
        // held is the better reason to give, and the two agree while it is.
        return heldSavegames.FindProfileHold(game.Identity) is null && game.PinnedRevision == held
            ? $" revision {held}, which is where this game is held"
            : $" revision {held}, which is what the savegame checked out there runs on";
    }

    public bool IsBusy(Repo repo, Game game)
    {
        return TargetRefs(repo, game).Any(x => leases.IsHeld(ResourceKeys.Target(x)));
    }

    public string Busy(Repo repo, Game game)
    {
        var holder = TargetRefs(repo, game)
            .Select(x => leases.DescribeHolder(ResourceKeys.Target(x)))
            .OfType<string>()
            .FirstOrDefault();

        return holder is null
            ? $"'{game.Name}' is busy. It was left as it is; try again in a moment."
            : $"{holder} is still running, so '{game.Name}' was left as it is. It will be free when that finishes.";
    }

    /// <summary>
    /// Every folder this game reaches, as the identities a lease is keyed by.
    /// </summary>
    /// <remarks>
    /// <b>Off the adapter rather than off a plan</b>, because the claim has to be made before planning
    /// - planning reads and hashes the folder, which is the slowest thing an apply does and exactly the
    /// part that must not run twice at once. A game with no mod capability reaches nothing and claims
    /// nothing, which is the same answer <see cref="TryPlanAsync"/> gives it.
    /// </remarks>
    private static IEnumerable<ModTargetRef> TargetRefs(Repo repo, Game game)
    {
        if (GetAdapter(repo, game) is not ILocalModAdapter adapter)
        {
            return [];
        }

        return adapter.ModTargets.Select(x => new ModTargetRef(game.Identity, x.Key));
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
    /// <para>
    /// The mod count is the task's proportion rather than the byte count: a sync is dozens of files of
    /// very different sizes, and a bar that jumped and stalled per file would be less informative than
    /// one that walks.
    /// </para>
    /// <para>
    /// <b>Each file being worked on is a subtask, so the bytes have somewhere to go.</b> That is
    /// exactly the case the outer bar cannot serve: a 900 MB download, or an archive being hashed
    /// during planning, is one tick of a count that then stands still for a minute. Most phases run
    /// one item at a time, so the next item's report ends the last; fetching runs several at once and
    /// says so, and each of its items ends with a report of its own.
    /// </para>
    /// </remarks>
    private sealed class SyncProgressRelay(IBackgroundTask task, IProgress<ModSyncProgress>? inner)
        : IProgress<ModSyncProgress>
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, IBackgroundSubtask> _subtasks = [];


        public void Report(ModSyncProgress value)
        {
            Track(value);

            // The phase alone. What used to be named here is the subtask's now, and naming it twice
            // would put the churn back on the line the subtasks exist to keep still.
            task.Report(value.Phase.ToString(), value.Completed, value.Total);

            inner?.Report(value);
        }

        private void Track(ModSyncProgress value)
        {
            var what = value.Detail ?? value.ModId;
            var name = what is null ? null : $"{value.Phase}: {what}";

            lock (_gate)
            {
                // Names carry the phase, so a new phase - whose first report is never concurrent,
                // or is the fetch phase's closing one with no item at all - ends everything left.
                if (value.Concurrent is false)
                {
                    foreach (var other in _subtasks.Keys.Where(x => x != name).ToList())
                    {
                        End(other);
                    }
                }

                if (name is null)
                {
                    return;
                }

                if (value.ItemFinished)
                {
                    End(name);

                    return;
                }

                if (_subtasks.TryGetValue(name, out var subtask) is false)
                {
                    subtask = task.BeginSubtask(name, value.TotalBytes >= ByteSize.LargeTransfer);
                    _subtasks[name] = subtask;
                }

                subtask.Report(
                    value.BytesTransferred,
                    value.TotalBytes,
                    value.TotalBytes > 0 ? ByteSize.Describe(value.BytesTransferred, value.TotalBytes) : null);
            }
        }

        private void End(string name)
        {
            if (_subtasks.Remove(name, out var subtask))
            {
                subtask.Dispose();
            }
        }
    }
}
