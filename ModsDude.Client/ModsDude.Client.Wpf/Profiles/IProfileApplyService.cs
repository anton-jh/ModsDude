using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Mods.Import;

namespace ModsDude.Client.Wpf.Profiles;

public interface IProfileApplyService
{
    /// <summary>
    /// Works out what would change, one plan per folder the game reaches.
    /// </summary>
    /// <remarks>
    /// <b>A list rather than a plan, because a game reaching three folders has three of them.</b>
    /// Sync's unit of work is genuinely one folder - see <see cref="ModSyncRequest"/> - so the loop
    /// lives here, at the thing that applies a profile to a <em>game</em>. No plans means there was
    /// nothing to plan: no mod capability, no folder configured, or none of them reachable right now.
    /// </remarks>
    /// <param name="revision">
    /// Which revision to plan against, or null to let the game decide - a past savegame held
    /// there pins the folder to its own revision, and everything else follows head. Named only by the
    /// check-out modal, which is previewing the apply for a savegame nothing is holding yet.
    /// </param>
    /// <param name="progress">
    /// Where to say which mod is being examined. Planning is not the quick half it was assumed to be -
    /// see <see cref="ModSyncService.PlanAsync"/> - so every caller that has somewhere to put a
    /// sentence passes one.
    /// </param>
    /// <param name="clearAll">
    /// Plan against an empty mod list, for clearing a game's folders rather than applying a profile to
    /// them. <paramref name="profileId"/>, <paramref name="profileName"/> and <paramref name="revision"/>
    /// are ignored.
    /// </param>
    Task<PlanAttempt> TryPlanAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        int? revision,
        CancellationToken cancellationToken,
        IProgress<ModSyncProgress>? progress = null,
        bool clearAll = false);

    /// <summary>
    /// Records that this game follows the profile, then applies it to every folder it reaches. One
    /// gesture.
    /// </summary>
    /// <remarks>
    /// <b>The intent is recorded here and nowhere else</b>, after the refusals and the confirmation
    /// and before any file moves - so a folder that could not be applied to leaves a game that still
    /// means to follow the profile, which is what the drift notice then carries. A caller that
    /// wanted the work without the decision wants <see cref="ApplyAsync"/>.
    /// </remarks>
    /// <inheritdoc cref="ApplyAsync" path="/param"/>
    /// <param name="pinRevision">
    /// Make <paramref name="revision"/> the game's standing intent rather than a one-off - see
    /// <see cref="Core.Persistence.PersistedGame.PinnedRevision"/>. Only following a friend who is on a past savegame
    /// does this; every other activation leaves the game on head, and clears a pin it had.
    /// </param>
    /// <param name="checkedOutSavegame">
    /// The savegame whose check-out this activation is part of, so friends hear about the check-out
    /// rather than about a re-apply of a profile the game may already have been on.
    /// </param>
    Task<ProfileApplyOutcome> ActivateAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision = null,
        bool pinRevision = false,
        Guid? checkedOutSavegame = null);

    /// <summary>
    /// Makes every folder this game reaches match the profile. Records nothing: whatever is being
    /// applied is already what the game follows.
    /// </summary>
    /// <param name="confirmPlan">
    /// Whether to show the plan before executing, once for the whole game. Moving a game onto a
    /// different profile uninstalls whatever the previous one put there and the reconciler knows
    /// exactly what that is, so it is shown rather than a bare "are you sure"; a re-apply of the
    /// profile the game is already on has nothing to disclose beyond the destructive part, which is
    /// confirmed either way. A caller whose own modal has already shown the plan passes false.
    /// </param>
    /// <param name="revision">
    /// Which revision to install, or null - nearly always - to let the game decide, per
    /// <see cref="TryPlanAsync"/>. Named by the activation a savegame check-out or copy runs first,
    /// which is preparing the folder for a savegame nothing is holding yet: a past one runs on its own
    /// revision, and letting the game decide would install head and leave the check-out that follows
    /// immediately drifted.
    /// </param>
    Task<ProfileApplyOutcome> ApplyAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken,
        int? revision = null);

    /// <summary>
    /// Takes the game off its profile, so ModsDude stops keeping its mod folders in step with one - the
    /// way to manage the mods by hand for a while without the drift notice objecting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two shapes, chosen by the user and not by this method.</b> Leaving the folders as they are
    /// is the ordinary one and touches no file: it is only the intent being withdrawn. Clearing them
    /// as well is the apply engine pointed at an empty list, so it is planned, confirmed and executed
    /// like any other apply - see <see cref="ModSyncRequest.ClearAll"/>.
    /// </para>
    /// <para>
    /// <b>Refused while a savegame with a profile is checked out</b>, for the reason a profile switch
    /// is: a game with no profile is one nobody is keeping in step with a mod list, and that savegame
    /// is exactly what the guard exists for. Asked before anything is planned, and the sync engine
    /// refuses a clear again as the backstop.
    /// </para>
    /// <para>
    /// <b>The intent is withdrawn after the refusals and the confirmation and before any file moves</b>,
    /// as it is recorded for an activation. A clear that fails part way leaves a game that means to
    /// follow nothing, which drifts from nothing - there is no notice to be left with, and finishing
    /// the job is another click on the same control.
    /// </para>
    /// </remarks>
    /// <param name="clearMods">Whether to also take every mod out of the game's folders.</param>
    Task<ProfileApplyOutcome> DeactivateAsync(
        Repo repo,
        Game game,
        bool clearMods,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// The reconciler's own plan as the confirmation. It already computes exactly what would change,
    /// so showing it beats asking "are you sure" about something the user cannot see.
    /// </summary>
    /// <param name="plans">
    /// Every folder the gesture would change, in one modal. A game reaching three of them is still
    /// one decision about one profile, so each folder gets a block of its own and the question is
    /// asked once - see the remarks on this class for why the alternative is unrepresentable.
    /// </param>
    /// <param name="clearing">
    /// Worded for emptying the folders rather than filling them: there is nothing to download, and
    /// "anything the profile does not pin" would be a sentence about a profile nobody is applying.
    /// </param>
    Task<bool> ConfirmPlanAsync(Game game, IReadOnlyList<ModSyncPlan> plans, bool clearing = false);

    /// <summary>
    /// The one interruption a re-apply is always worth: files nothing else on the machine has a copy
    /// of, named, with where they are going.
    /// </summary>
    /// <param name="plans">
    /// Every folder being applied to, because this is one question about one gesture and a file is
    /// no less unrecoverable for being in the second folder. Named across all of them; the counts
    /// are the sum.
    /// </param>
    /// <returns>
    /// Where to put them, or null where the user backed out. Which of the two answers is in the choice
    /// itself - a null <see cref="UnrecognisedFilesChoice.Folder"/> is the Recycle Bin.
    /// </returns>
    Task<UnrecognisedFilesChoice?> ConfirmUnrecognisedAsync(IReadOnlyList<ModSyncPlan> plans);

    /// <summary>
    /// Whether an apply to this game would be refused right now because something else is mid-apply
    /// on one of its folders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a <c>CanExecute</c>, which is what turns the refusal from a message into a greyed button.
    /// Pages re-ask it whenever <see cref="IResourceLeases.Changed"/> fires.
    /// </para>
    /// <para>
    /// <b>A hint, not the guard.</b> It reads the leases without taking one, so it is already out of
    /// date by the time a button is redrawn from it - which is fine, because
    /// <see cref="ProfileApplyService.RunAsync"/> asks properly and refuses properly. Treating this as the guard is how a
    /// second apply gets in.
    /// </para>
    /// </remarks>
    bool IsBusy(Repo repo, Game game);

    /// <summary>
    /// What to say to somebody whose apply was refused: the work that is already running, named.
    /// </summary>
    /// <remarks>
    /// The first holder found rather than all of them. A game with three folders blocked by one other
    /// gesture would otherwise say the same sentence three times, and the useful half of the message is
    /// what to wait for, not how many ways it overlaps.
    /// </remarks>
    string Busy(Repo repo, Game game);
}
