using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// The verbs that move a savegame between a slot and the repo - check out, check in, discard and
/// publish - with the dialogs they need, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="ProfileApplyService"/>, and for the same reason: check-in is reached
/// from the repo's Saves list <em>and</em> from the check-out dialog's way out of a refused slot,
/// and two copies of "ask, send, resolve a stale base" would be two copies that eventually disagree
/// about what force means.
/// </para>
/// <para>
/// <b>Check-out and publish are here too</b>, because a profile's Overview offers them for its own
/// savegame as well as the Saves list for every savegame. The pages keep what is theirs - which rows
/// they draw and when they re-read them - and hand in a callback for the moment the repo changed.
/// </para>
/// <para>
/// The modal host is taken lazily because it is the shell itself, which is composed from services that
/// may want this one - see <see cref="ProfileApplyService"/> for the cycle that closes otherwise.
/// </para>
/// </remarks>
public sealed class SavegameFlowService(
    ISavegameService savegames,
    ISavegamesClient savegamesClient,
    ProfileService profileService,
    SyncManifestStore manifestStore,
    SavegameBindingStore bindingStore,
    ProfileApplyService applyService,
    DriftMonitor driftMonitor,
    ShellNavigationService shellNavigation,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts)
{
    /// <summary>What a savegame's profile is called where this member cannot see it.</summary>
    public const string UnseenProfileName = "A profile you cannot see";

    /// <summary>
    /// Whether a locked pin moved between two revisions of one profile, keyed by the pair. Revisions do
    /// not change once written, so an answer is good for the session - and a check-out dialog and a
    /// row chip ask the same question about the same pair.
    /// </summary>
    private readonly Dictionary<(Guid ProfileId, int From, int To), bool> _lockedDrift = [];

    /// <summary>
    /// The number the player knows a held savegame's slot by, for a game that numbers them - what the
    /// dialogs say instead of leaving the player to work out which folder is meant.
    /// </summary>
    private int? HeldSlotNumber(Game game, Guid savegameId)
        => savegames.GetBinding(game, savegameId) is SavegameCheckoutBinding binding
            ? savegames.DescribeSlotNumber(game, binding.Slot)
            : null;

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Asks, uploads, and turns a refused base into a choice rather than an error.
    /// </summary>
    /// <param name="savegameName">What the dialog calls the save. Display text only - see <paramref name="renameTo"/>.</param>
    /// <param name="renameTo">
    /// What to write into the slot as the save's name before packing it, or null to leave the slot's
    /// own name alone. Deliberately separate from <paramref name="savegameName"/>: a caller that is
    /// showing a placeholder there because it does not actually know this savegame's name must not
    /// have that placeholder land in the save file - see <c>RepoSavegamesPageViewModel.CheckInBlockingAsync</c>.
    /// </param>
    /// <returns>
    /// The snapshot that was minted, or null where nothing was - the user backed out, the save had not
    /// changed, or they chose to look at the newer snapshot first.
    /// </returns>
    public async Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        CancellationToken cancellationToken,
        string? renameTo = null)
    {
        var modal = new SavegameCheckInModalViewModel(
            savegameName, slotLabel, DescribePlayedOn(game, savegameId), HeldSlotNumber(game, savegameId));

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return SavegameCheckInOutcome.Cancelled;
        }

        return await SendAsync(game, savegameId, savegameName, modal.TrimmedLabel, modal.KeepPlaying, force: false, cancellationToken, renameTo);
    }

    /// <summary>
    /// Hands the claim back without minting anything - taken by mistake, never played.
    /// </summary>
    /// <remarks>
    /// The confirmation carries what it costs, because this is the one verb with no snapshot behind it:
    /// whatever is in the slot is gone, and the Recycle Bin is the only way back.
    /// </remarks>
    public async Task<bool> DiscardAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        bool hasUnpublishedPlay,
        CancellationToken cancellationToken)
    {
        var slot = SavegameSlotWording.Named(HeldSlotNumber(game, savegameId), slotLabel);
        var consequence = hasUnpublishedPlay
            ? $"{Capitalised(slot)} has been played since it was downloaded, and none of that has been checked in. " +
              "It goes to the Recycle Bin and no snapshot is minted, so the only copy of that play is one you restore by hand."
            : $"{Capitalised(slot)} goes to the Recycle Bin and no snapshot is minted. The savegame goes back to being anybody's to take.";

        var modal = new ConfirmationDialogViewModel(
            $"Give '{savegameName}' back without checking it in?",
            consequence,
            hasUnpublishedPlay ? IconKind.Warning : IconKind.Question,
            "Discard it - the local copy goes to the Recycle Bin",
            "Keep it checked out");

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return false;
        }

        using var task = backgroundTasks.Begin($"Giving '{savegameName}' back", "Releasing the claim, then recycling the local copy");

        await savegames.DiscardAsync(game, savegameId, cancellationToken);

        return true;
    }

    /// <summary>
    /// Cuts the local tie and leaves the save where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One situation now, where there were two.</b> The other was a hold whose savegame the repo
    /// had deleted for good - nothing to hand back, nothing to check in to, and no reason on earth
    /// for anybody to answer no. Those are dropped on sight by
    /// <c>RepoSavegamesPageViewModel.ForgetDeletedHoldsAsync</c> rather than turned into a question,
    /// which is what a choice with one sane answer always is.
    /// </para>
    /// <para>
    /// What is left is the hold in a folder the settings no longer name. The save is real, the claim
    /// is real and still taken, and nothing that touches the bytes can run - so this is the only way
    /// out short of putting the folder back, and the confirmation says exactly what stays behind.
    /// </para>
    /// <para>
    /// Nothing in the slot is touched, which is the difference from Discard: that one recycles the
    /// local copy, this one leaves an ordinary save of the user's own behind.
    /// </para>
    /// </remarks>
    /// <param name="folderName">
    /// What the folder holding it is called, where the caller could name it - by key, since no
    /// adapter offers the folder any more. Null falls back to a sentence that names no folder.
    /// </param>
    /// <returns>False where the dialog was dismissed, or there was nothing to forget.</returns>
    public async Task<bool> DisconnectAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string? folderName)
    {
        var where = folderName is string named ? $"the '{named}' folder" : "a folder this game's settings no longer name";

        var modal = new ConfirmationDialogViewModel(
            $"Stop tracking '{savegameName}'?",
            $"Your copy stays exactly where it is, in {where}, and becomes an ordinary save of your own - ModsDude stops recognising it. "
              + $"The claim on '{savegameName}' is not handed back, so nobody else can take it until you do. "
              + "Point the settings back at that folder instead if you want to check it in.",
            IconKind.Warning,
            "Stop tracking it - nothing on disk changes",
            "Leave it connected");

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return false;
        }

        return savegames.Forget(game, savegameId);
    }

    /// <summary>
    /// Hands a save back from the game holding it: the check-in, what it did said as a toast, and the
    /// drift check it moves.
    /// </summary>
    /// <remarks>
    /// <b>The game is the hold's, not a choice.</b> A check-in uploads what is in a slot, so the only
    /// game it can mean is the one whose slot holds the copy - which is why there is no picker here the
    /// way there is for a check-out.
    /// </remarks>
    /// <param name="changed">Called once a snapshot landed, so the caller can re-read.</param>
    public async Task CheckInHeldAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        Func<Task> changed,
        CancellationToken cancellationToken)
    {
        try
        {
            // The savegame's name where the dialog wants a slot label, as CheckInBlockingAsync does:
            // the slot's own id is a folder name the player has never thought in, and what they are
            // handing back is the save rather than the folder. It is this savegame's own record, so it
            // also stands as the name to write into the slot before it is packed.
            var outcome = await CheckInAsync(game, savegameId, savegameName, savegameName, cancellationToken, renameTo: savegameName);

            if (outcome.WasDeferred)
            {
                toasts.Show($"Left as it is. Your copy of '{savegameName}' is still in its slot and still yours.");

                return;
            }

            if (outcome.Succeeded is false)
            {
                return;
            }

            toasts.Show(outcome.KeptPlaying
                ? $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server. The save is still in '{game.Name}' and still yours."
                : $"Snapshot {outcome.Snapshot!.Number} of '{savegameName}' is on the server, and the save is anybody's to take.");

            await driftMonitor.CheckAsync();
            await changed();
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-upload. The check-in either landed or it did not, and the next read
            // of the page says which - there is no page left to say it on now.
        }
        catch (Exception exception)
        {
            // Reached from a click that raises a plain event rather than running a command, so a
            // failure here has no command to carry it to the global handler.
            await errorReporter.ShowAsync(exception, "checking a savegame in");
        }
    }


    /// <summary>
    /// The game this repo's savegames act on, with what it is holding and what its mod folder was last
    /// synced to - the two facts <see cref="SavegameRowRules.Describe"/> needs. Null where nothing is
    /// connected here.
    /// </summary>
    /// <remarks>
    /// <b>One, not a list to choose from.</b> A game is keyed by its identity and a repo is about one
    /// game, so there is nothing to rank. Read once per list rather than once per row: a manifest is
    /// every mod in the profile with a hash each, and the unreachable holds hydrate the adapter.
    /// </remarks>
    public SavegameHost? ReadHost(Repo repo)
    {
        if (repo.Games.FirstOrDefault() is not Game game)
        {
            return null;
        }

        var manifest = manifestStore.TryReadAgreed(game.TargetRefs);

        return new SavegameHost(
            game,
            bindingStore.GetBindings(game.Identity),
            savegames.GetUnreachableHolds(game).Select(x => x.SavegameId).ToHashSet(),
            manifest?.ProfileId,
            manifest?.ProfileRevision);
    }

    /// <summary>
    /// Tells a row what its buttons can do, and where the local copy of the save is.
    /// </summary>
    /// <remarks>
    /// <b>Two questions, still.</b> Whether the game would accept a check-out and whether it is
    /// already holding this save are different facts - a claim taken on the desktop is still yours on
    /// the laptop, and there is nothing here to check in - so they are answered separately even now
    /// that both are about the same installation.
    /// </remarks>
    /// <param name="nameOf">
    /// What a savegame in the way is called, read off the caller's own list - null where it is not in
    /// it, and the refusal stands without the name.
    /// </param>
    public void Offer(Repo repo, SavegameListItemViewModel row, SavegameHost? host, Func<Guid, string?> nameOf)
    {
        if (host is null)
        {
            // Nothing connected: no buttons work, and the row says so rather than the rule doing it.
            // Whether there is a game to act on is not a fact about this savegame.
            row.SetHeldHere(null);
            row.SetOffer(null, null);

            return;
        }

        row.SetHeldHere(FindHold(row.Id, host));

        var offer = SavegameRowRules.Describe(
            row.Id,
            row.Savegame.ProfileId,
            FindProfile(repo, row.Savegame.ProfileId)?.HeadRevision,
            row.PinnedRevision,
            host.Held,
            host.AppliedProfileId,
            host.AppliedRevision);

        row.SetOffer(
            offer,
            offer.CanCheckOut || offer.BlockingSavegameId == Guid.Empty ? null : nameOf(offer.BlockingSavegameId));
    }

    /// <summary>
    /// Where the local copy of one savegame is sitting, or null where this machine holds none.
    /// </summary>
    private SavegameHoldHere? FindHold(Guid savegameId, SavegameHost host)
    {
        // Written out rather than FirstOrDefault because a binding is a struct: the default is a
        // fully-formed one with a blank slot reference, and a row handed that would offer to
        // disconnect a hold that does not exist.
        foreach (var binding in host.Held)
        {
            if (binding.SavegameId != savegameId)
            {
                continue;
            }

            return new SavegameHoldHere(
                host.Game,
                binding.Slot,
                savegames.DescribeFolder(host.Game, binding.Slot.Target),
                host.UnreachableHolds.Contains(savegameId),
                savegames.DescribeSlotNumber(host.Game, binding.Slot));
        }

        return null;
    }

    /// <summary>
    /// Whether any locked pin moved between two revisions. An unlocked mod at a different version is
    /// untidy; a locked map at a different version is a damaged save, and only the second is worth
    /// colouring a chip for.
    /// </summary>
    public async Task<bool> LockedPinMovedAsync(Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken)
    {
        if (_lockedDrift.TryGetValue((profileId, from, to), out var cached))
        {
            return cached;
        }

        try
        {
            var comparison = await profileService.CompareRevisions(repoId, profileId, from, to, cancellationToken);

            var moved = comparison.Changes.Any(x => x.VersionMoved && (x.FromLocked || x.ToLocked || x.Version.Locked));

            _lockedDrift[(profileId, from, to)] = moved;

            return moved;
        }
        catch (ApiException)
        {
            // The count is still true and still worth showing; only the colour is unknown, and the
            // quiet answer is the right one to guess when it is. Not remembered: this object lives as
            // long as the app, and a dropped connection is not an answer about two revisions.
            return false;
        }
    }


    /// <summary>
    /// Checks a savegame out, or takes a copy of it: the slot dialog, then the claim, then the mods.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The destructive step is local and comes first, the claim is social and wants to be fast, and the
    /// mod question is last because it is the only one that can be deferred. This is that order.
    /// </para>
    /// <para>
    /// Where the snapshot is not the head it is a restore first - copied forward as a new snapshot,
    /// with nothing in between deleted - which is why there is no separate restore flow to find.
    /// </para>
    /// </remarks>
    /// <param name="currentUserId">
    /// Who is asking, so that a claim of their own is not taken from "somebody". Null where it could not
    /// be read, and then any holder is asked about - the same as the Saves row.
    /// </param>
    /// <param name="nameOf">What a savegame is called, read off the caller's own list. Null where it is not in it.</param>
    /// <param name="changed">
    /// Called whenever the repo moved under the caller: after the check-out, and after checking in the
    /// savegame that was in the way - before the dialog is offered again, so the caller's list is the
    /// one it names savegames from.
    /// </param>
    public async Task CheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        CancellationToken cancellationToken)
    {
        try
        {
            await StartCheckOutAsync(
                repo, savegame, snapshotNumber, mode, currentUserId, nameOf, changed, agreedToTakeFrom: null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Navigated away. Nothing was written, and there is no page left to say so on.
        }
        catch (Exception exception)
        {
            // Reached from a click that raises a plain event rather than running a command, so a
            // failure here has no command to carry it to the global handler.
            await errorReporter.ShowAsync(exception, "checking a savegame out");
        }
    }

    /// <param name="agreedToTakeFrom">
    /// Whose claim the user has already agreed to take, on the way round a refused slot - so coming back
    /// here does not ask them the same question twice.
    /// </param>
    private async Task StartCheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        if (savegame.Head is null || snapshotNumber <= 0)
        {
            await modalService.Value.Show(ConfirmationDialogViewModel.Refusal(
                $"'{savegame.Name}' has no snapshot yet",
                "Nothing has been checked in for this savegame, so there is nothing to write into a slot."));

            return;
        }

        if (repo.Games.FirstOrDefault() is not Game game)
        {
            await modalService.Value.Show(ConfirmationDialogViewModel.Refusal(
                "No game is connected here",
                $"A savegame has to be written into an installation of the game. Use 'Connect game' in {repo.Name} first."));

            return;
        }

        // Taking a save from somebody is allowed, and is decided on a screen naming them - before the
        // slot question, because this is the one that decides whether there is a check-out at all. A
        // copy takes nothing from anybody, and a claim of your own is not somebody else's.
        var holder = savegame.Checkout is SavegameCheckoutDto checkout && checkout.Status is not SavegameCheckoutStatus.Ended
            ? checkout
            : null;
        var heldByMe = holder is not null && currentUserId is not null && holder.User.Id == currentUserId;
        var takingFrom = mode is SavegameCheckOutMode.CheckOut && heldByMe is false ? holder : null;

        if (takingFrom is not null && takingFrom.User.Id != agreedToTakeFrom)
        {
            var confirmation = ConfirmTakeOver(savegame.Name, takingFrom);

            await modalService.Value.Show(confirmation);

            if (confirmation.Result is false)
            {
                return;
            }
        }

        // After the take-over question and before the slot one: whether there is a check-out at all
        // is decided first, and the slot dialog's mod summary should describe the folder as it will
        // be rather than as it was.
        if (await ActivateFirstAsync(repo, game, savegame, mode, changed, cancellationToken) is false)
        {
            return;
        }

        var context = await BuildCheckOutContextAsync(repo, savegame, game, mode, nameOf, cancellationToken);

        var modal = new SavegameCheckOutModalViewModel(
            mode,
            savegame.Name,
            FindProfile(repo, savegame.ProfileId)?.Name ?? UnseenProfileName,
            snapshotNumber,
            savegame.Head.Number,
            context);

        await modalService.Value.Show(modal);

        if (modal.CheckInFirstSavegameId is Guid blocking)
        {
            await CheckInBlockingAsync(
                repo, game, blocking, savegame, snapshotNumber, mode, currentUserId, nameOf, changed, takingFrom?.User.Id, cancellationToken);

            return;
        }

        if (modal.Result is not SavegameSlotOptionViewModel slot)
        {
            return;
        }

        await ExecuteCheckOutAsync(repo, savegame, snapshotNumber, mode, game, slot, changed, takingFrom?.User.Id, cancellationToken);
    }

    /// <summary>
    /// The question asked before taking a save somebody else has checked out.
    /// </summary>
    /// <remarks>
    /// <b>A warning that names the person, and says what it costs.</b> Taking it is always allowed - the
    /// claim is advisory - so this is not a refusal in disguise; but the moment it is taken there are
    /// two copies of one save, and whoever checks in second overwrites the other. That is the sentence
    /// worth reading before the click rather than after it.
    /// </remarks>
    private static ConfirmationDialogViewModel ConfirmTakeOver(string savegameName, SavegameCheckoutDto holder)
    {
        var name = holder.User.DisplayName;

        return new ConfirmationDialogViewModel(
            $"{name} has '{savegameName}' checked out",
            $"They have had it since {SavegameWording.Exactly(holder.TakenAt)}. Checking it out takes it from them, "
                + "and their ModsDude will tell them.\n\n"
                + "If they are playing it, you will each have a copy of the same save: whoever checks in second has "
                + "to force it, and that overwrites the other's play.",
            IconKind.Warning,
            $"Take it from {name}",
            "Leave it with them");
    }

    /// <summary>
    /// Where the mod folder is not on the revision this savegame runs on, asks to activate its profile
    /// and does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked rather than refused.</b> Check out used to be disabled with "Apply X first" on it, beside
    /// an Apply profile button - a click the user always had to make next anyway. The question is the
    /// part worth keeping: an activation can move and recycle mods, so it is said before it happens.
    /// </para>
    /// <para>
    /// <b>A copy may decline it and go ahead.</b> A check-out holds the save against this folder, so the
    /// folder has to be right; a copy claims nothing and is an ordinary save of the user's own
    /// afterwards, so writing it next to whatever the folder has now is theirs to choose.
    /// </para>
    /// <para>
    /// <b>The same rule the row drew its button from</b>, read again rather than passed in: the Overview
    /// and the Saves list both come here, and the folder may have moved since either drew itself.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Whether to carry on - nothing needed doing, the activation finished, or a copy was told to leave
    /// the folder alone.
    /// </returns>
    private async Task<bool> ActivateFirstAsync(
        Repo repo,
        Game game,
        SavegameDto savegame,
        SavegameCheckOutMode mode,
        Func<Task> changed,
        CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false
            || FindProfile(repo, savegame.ProfileId) is not ProfileDto profile
            || ReadHost(repo) is not SavegameHost host)
        {
            return true;
        }

        var pinned = SavegameService.TargetRevisionOf(savegame);

        var offer = SavegameRowRules.Describe(
            savegame.Id, profile.Id, profile.HeadRevision, pinned, host.Held, host.AppliedProfileId, host.AppliedRevision);

        if (offer.ActivatesFirst is false)
        {
            return true;
        }

        var list = SavegameRowRules.DescribeActivation(profile.Name, pinned);

        var confirmation = mode is SavegameCheckOutMode.TakeCopy
            ? new ConfirmationDialogViewModel(
                $"Activate {list} first?",
                $"'{savegame.Name}' runs on {list}, and the mod folder in '{game.Name}' is not on it. "
                    + $"Activating it first puts the mods the copy was saved with in place. Leaving it writes the copy "
                    + "next to whatever mods the folder has now, which the game may not load it with.",
                IconKind.Question,
                "Activate it, then take the copy",
                "Cancel",
                "Leave the mods as they are")
            : new ConfirmationDialogViewModel(
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
        // head - wrong for a past savegame, whose check-out would then leave the folder drifted.
        var outcome = await applyService.ActivateAsync(
            repo, game, profile.Id, profile.Name, confirmPlan: false, progress: null, cancellationToken,
            revision: pinned ?? profile.HeadRevision);

        await driftMonitor.CheckAsync();

        // The folder moved, so every row's answer about it has too - including where the check-out
        // is abandoned at the slot dialog that follows.
        await changed();

        if (outcome.Succeeded is false)
        {
            toasts.Show(outcome.Message, outcome.ToastSeverity);

            return false;
        }

        return true;
    }

    /// <summary>
    /// The way out of a refused slot: check the savegame occupying it in, then offer the dialog again
    /// with the slot free. One action rather than a warning, per docs/PLAN.md#slot-safety.
    /// </summary>
    private async Task CheckInBlockingAsync(
        Repo repo,
        Game game,
        Guid blockingSavegameId,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        var blockingName = nameOf(blockingSavegameId);

        var outcome = await CheckInAsync(
            game,
            blockingSavegameId,
            blockingName ?? "that savegame",
            blockingName ?? "the slot",
            cancellationToken,
            // Null rather than the placeholder above where the caller's own list does not have the
            // savegame: a name only worth showing in a sentence is not one worth writing into the save.
            renameTo: blockingName);

        if (outcome.ReleasedTheSlot is false)
        {
            toasts.Show(
                outcome.WasDeferred
                    ? "That savegame was left checked out, so its slot is still taken."
                    : "That savegame is still checked out, so its slot is still taken.",
                ToastSeverity.Warning);

            return;
        }

        await changed();

        // Read again rather than reused: the savegame's own state is what the dialog describes, and a
        // check-in a moment ago is exactly the kind of thing that moves it.
        var refreshed = (await savegamesClient.GetSavegamesV1Async(repo.Id, cancellationToken))
            .FirstOrDefault(x => x.Id == savegame.Id);

        if (refreshed is not null)
        {
            await StartCheckOutAsync(
                repo, refreshed, snapshotNumber, mode, currentUserId, nameOf, changed, agreedToTakeFrom, cancellationToken);
        }
    }

    private async Task ExecuteCheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        Game game,
        SavegameSlotOptionViewModel slot,
        Func<Task> changed,
        string? agreedToTakeFrom,
        CancellationToken cancellationToken)
    {
        var name = savegame.Name;

        // Downloading and unpacking a save is the slow half of both verbs, and both are safe to walk
        // away from - the claim, where there is one, is taken before the bytes move.
        using var task = backgroundTasks.Begin(
            mode is SavegameCheckOutMode.TakeCopy
                ? $"Copying '{name}' into '{game.Name}'"
                : $"Checking '{name}' out into '{game.Name}'",
            $"Snapshot {snapshotNumber}");

        task.DeclareTransfers(TransferDirection.Download);

        if (mode is SavegameCheckOutMode.TakeCopy)
        {
            await savegames.TakeCopyAsync(
                game, savegame, snapshotNumber, slot.Ref, cancellationToken, new SavegameStripProgress(task));

            toasts.Show($"Snapshot {snapshotNumber} of '{name}' is in '{game.Name}'. Nobody was stopped from playing it, " +
                        "and this machine holds no claim on it - the slot is an ordinary save of your own now.");

            return;
        }

        // Restoring copies forward, so an old snapshot becomes the head and the check-out that follows
        // has no stale base to reason about. Nothing in between is deleted.
        if (snapshotNumber != savegame.Head?.Number)
        {
            task.Report($"Restoring snapshot {snapshotNumber} as the newest one");

            await savegamesClient.RestoreSavegameSnapshotV1Async(
                repo.Id, savegame.Id, snapshotNumber, new RestoreSavegameSnapshotRequest(), cancellationToken);

            var refreshed = await savegamesClient.GetSavegamesV1Async(repo.Id, cancellationToken);

            savegame = refreshed.FirstOrDefault(x => x.Id == savegame.Id) ?? savegame;
        }

        task.Report("Taking the claim");

        var takenFrom = await savegames.CheckOutAsync(game, savegame, slot.Ref, cancellationToken, new SavegameStripProgress(task));

        if (takenFrom is null)
        {
            toasts.Show($"'{name}' is checked out to you, in '{game.Name}'.");
        }
        else if (takenFrom.UserId == agreedToTakeFrom)
        {
            toasts.Show($"'{name}' is checked out to you, in '{game.Name}'. {takenFrom.DisplayName} no longer has it, " +
                        "and their ModsDude will tell them.");
        }
        else
        {
            // The list this was started from was behind the server: somebody took the save after it was
            // read, so the question above was never asked about them. The server's answer is what says
            // so, and the claim is taken by now - all that is left is to say whose it was.
            toasts.Show($"{takenFrom.DisplayName} had '{name}' checked out since {SavegameWording.Exactly(takenFrom.TakenAt)} - " +
                        $"the list you started from did not show it yet. It is yours now, in '{game.Name}', and their ModsDude will tell them.",
                        ToastSeverity.Warning);
        }

        await ApplyProfileAsync(repo, game, savegame, cancellationToken);

        await changed();
    }

    /// <summary>
    /// The mod half, last and separately. Checking out a save derives and applies its profile where the
    /// adapter has mods, and is simply "write the slot" where it does not - and a user who wanders off
    /// after the claim still holds the save and has it on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Which revision is not decided here.</b> The binding was written a moment ago and carries what
    /// this savegame runs on - head for the profile's current savegame, its own pinned revision for a past
    /// one - and every apply resolves it from there. Working it out a second time in this method is how
    /// the check-out comes to install a different list from the one the drift check then expects.
    /// </para>
    /// <para>
    /// <b>And it is the ordinary activation.</b> Declining is declining: what keeps the state visible is
    /// the savegame half of the drift check, which is exactly the thing that fires here - the save this
    /// machine now holds follows a mod list the folder is not on.
    /// </para>
    /// </remarks>
    private async Task ApplyProfileAsync(Repo repo, Game game, SavegameDto savegame, CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false)
        {
            return;
        }

        if (FindProfile(repo, savegame.ProfileId) is not ProfileDto profile)
        {
            return;
        }

        // An activation: the game is being put on the mod list the save this machine just took
        // follows. The service refuses, discloses, records and works, in that order - including its
        // own naming of any files nothing else has a copy of.
        // Named as a check-out so friends hear about it as one, even where the game was already on
        // this profile and nothing about the activation itself is news.
        var outcome = await applyService.ActivateAsync(
            repo, game, profile.Id, profile.Name, confirmPlan: false, progress: null, cancellationToken,
            checkedOutSavegame: savegame.Id);

        toasts.Show(outcome.Message, outcome.ToastSeverity);

        await driftMonitor.CheckAsync();

        if (outcome.Status is ProfileApplyStatus.Declined)
        {
            await OfferModListReviewAsync(repo, game, profile, cancellationToken);
        }
    }

    /// <summary>
    /// The way out of a declined apply: open the profile's mod list with the folder that stopped it
    /// already scanned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason somebody declines here is nearly always the same one - the folder holds mods the
    /// repo has never seen, and they would rather import them than have them recycled. That is the
    /// editor's job, so the offer is worth making rather than leaving them to find it.
    /// </para>
    /// <para>
    /// <b>A second, small question rather than a third button on the first.</b> Folding it into the
    /// service's own disclosure would mean either a three-way dialog or listing the same files a
    /// second time - and the plan is only re-read on this path, which is the uncommon one. Nothing is
    /// recorded either way: the user said no to the apply.
    /// </para>
    /// </remarks>
    private async Task OfferModListReviewAsync(Repo repo, Game game, ProfileDto profile, CancellationToken cancellationToken)
    {
        // The first folder with something unrecognised in it, since that is the one whose contents
        // the user is about to read. A decline for any other reason finds none and asks nothing.
        // On the strip because planning reads and hashes the mod folder - see ModSyncService.PlanAsync -
        // and this one runs between two dialogs, where a still window reads as the app having stopped.
        using var task = backgroundTasks.Begin($"Checking what '{profile.Name}' would change");

        var plans = await applyService.TryPlanAsync(
            repo, game, profile.Id, profile.Name, revision: null, cancellationToken, ProfileApplyService.Report(task, null));

        if (plans.FirstOrDefault(x => x.Unrecognised.Count > 0) is not ModSyncPlan plan)
        {
            return;
        }

        var choice = new ConfirmationDialogViewModel(
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

    /// <summary>
    /// Everything the check-out dialog needs about one game: its slots and their safety, what the mod
    /// folder would have to do, and how far the save's revision is from the profile's.
    /// </summary>
    private async Task<SavegameCheckOutContext> BuildCheckOutContextAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        SavegameCheckOutMode mode,
        Func<Guid, string?> nameOf,
        CancellationToken cancellationToken)
    {
        var slots = await savegames.GetSlotsAsync(game, cancellationToken);
        var options = new List<SavegameSlotOptionViewModel>();

        foreach (var slot in slots)
        {
            var availability = await savegames.ClassifySlotAsync(game, slot.Ref, cancellationToken);
            var binding = bindingStore.GetBindingForSlot(game.Identity, slot.Ref);

            options.Add(new SavegameSlotOptionViewModel(
                slot,
                availability,
                binding?.SavegameId,
                binding is SavegameCheckoutBinding held ? nameOf(held.SavegameId) : null));
        }

        var suggested = await savegames.SuggestSlotAsync(game, savegame.Id, cancellationToken);
        var hint = bindingStore.GetSlotHint(game.Identity, savegame.Id);

        return new SavegameCheckOutContext(
            options,
            suggested,
            DescribeSuggestion(options, suggested, hint),
            mode is SavegameCheckOutMode.CheckOut
                ? await BuildModsSummaryAsync(repo, savegame, game, cancellationToken)
                : null,
            await BuildRevisionNoteAsync(repo, savegame, cancellationToken),
            // Absent for a copy, which applies nothing: the slot is written and the mod folder is left
            // exactly as it was, so there is no list the save is about to run on.
            mode is SavegameCheckOutMode.CheckOut ? DescribeRunsOn(repo, savegame) : null);
    }

    /// <summary>
    /// Which revision the folder will be on afterwards, in one line.
    /// </summary>
    /// <remarks>
    /// <b>Worth showing even for a current savegame</b>, where the number can differ from the one the
    /// savegame was last played on whenever anybody has edited the profile since - and that is precisely
    /// the case where somebody wants to have seen the number before the evening rather than after it.
    /// </remarks>
    private string? DescribeRunsOn(Repo repo, SavegameDto savegame)
    {
        if (SavegameService.TargetRevisionOf(savegame) is int pinned)
        {
            return $"This savegame stays on rev {pinned}. Playing it does not move it forward.";
        }

        return FindProfile(repo, savegame.ProfileId) is ProfileDto profile
            ? $"Will run on {profile.Name} rev {profile.HeadRevision}."
            : null;
    }

    /// <summary>
    /// Why the pre-selection is what it is, said plainly - and nothing at all in the ordinary case,
    /// where the slot this save was last in is free and the sentence would only be noise.
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

        // Gone covers the folder having gone as well as the slot: a target somebody took out of the
        // settings takes every slot in it with it, and "the slot this save was last in is gone" is
        // the same sentence for both.
        return taken is null
            ? "The slot this save was last in is gone, so the first free one is picked instead."
            : $"The slot this save was last in now holds '{taken.Label}', so the first free one is picked instead.";
    }

    /// <summary>
    /// What the mod folder would have to do. Null where the adapter has no mods or the folder cannot be
    /// read - the section is absent rather than saying nothing at length.
    /// </summary>
    private async Task<SavegameModsSummary?> BuildModsSummaryAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        CancellationToken cancellationToken)
    {
        if (repo.Adapter.CanSupportMods is false || FindProfile(repo, savegame.ProfileId) is not ProfileDto profile)
        {
            return null;
        }

        // Named rather than resolved from the game: nothing is holding this savegame yet, so the
        // game has no opinion about it - and the plan shown here has to be the plan that runs.
        // On the strip for the same reason the apply's own planning is: this reads and hashes the mod
        // folder, and it runs while somebody is waiting for the check-out dialog to open.
        using var task = backgroundTasks.Begin($"Checking what '{savegame.Name}' would need");

        var plans = await applyService.TryPlanAsync(
            repo,
            game,
            profile.Id,
            profile.Name,
            SavegameService.TargetRevisionOf(savegame),
            cancellationToken,
            ProfileApplyService.Report(task, null));

        if (plans.Count == 0)
        {
            return null;
        }

        // Summed across the folders, because what is being previewed is what checking this savegame
        // out does to the game - which is every folder it reaches.
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

        // A rename leaves the bytes alone, so a locked mod being renamed is not a mod changing
        // under a savegame and is not worth warning about.
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
    /// Which revision the save was last played on against the one the profile is now at. Absent where
    /// they are the same, which is the common case and the one worth saying nothing about.
    /// </summary>
    private async Task<SavegameRevisionNote?> BuildRevisionNoteAsync(Repo repo, SavegameDto savegame, CancellationToken cancellationToken)
    {
        if (savegame.Head is not SavegameSnapshotDto head ||
            head.ProfileRevision is not int played ||
            FindProfile(repo, savegame.ProfileId) is not ProfileDto profile ||
            profile.HeadRevision <= played)
        {
            return null;
        }

        var moved = await LockedPinMovedAsync(repo.Id, profile.Id, played, profile.HeadRevision, cancellationToken);

        var text = $"Last played on revision {played}; {profile.Name} is now at {profile.HeadRevision}.";

        return new SavegameRevisionNote(
            moved
                ? text + " A locked mod moved between them, and hosting this save on it may damage it."
                : text,
            moved);
    }

    /// <summary>
    /// The profile a savegame follows, or <c>null</c> where it follows none - the same answer as a
    /// profile this member cannot see, and deliberately so.
    /// </summary>
    private ProfileDto? FindProfile(Repo repo, Guid? profileId)
        => profileId is Guid id
            ? profileService.Profiles.FirstOrDefault(x => x.Id == id && x.RepoId == repo.Id)
            : null;

    /// <summary>
    /// Makes a savegame out of a save that is already on this disk: which slot, then the publish dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The slot is what it is about</b>, so it is asked for first: the same flat slot list across
    /// every savegame folder the game reaches, filtered to the ones ModsDude has no copy of. Everything
    /// after that - the name, the mod list, the revision this first snapshot declares - is the publish
    /// dialog's.
    /// </para>
    /// <para>
    /// Failures are reported here rather than thrown, because both callers reach this from a click and
    /// have nothing to add to the sentence.
    /// </para>
    /// </remarks>
    /// <param name="preselectProfileId">
    /// The profile the dialog opens on, where the caller is a profile's own page. Null opens it on the
    /// profile this game follows - the dialog still offers every answer either way.
    /// </param>
    /// <param name="published">Called with the new savegame's id once it exists, so the caller can re-read.</param>
    public async Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Func<Guid, Task> published,
        CancellationToken cancellationToken)
    {
        if (repo.Games.FirstOrDefault() is not Game game)
        {
            await modalService.Value.Show(ConfirmationDialogViewModel.Refusal(
                "No game is connected here",
                $"Publishing takes a save that is already on this machine, so there has to be an installation of the game to take one from. Use 'Connect game' in {repo.Name} first."));

            return;
        }

        try
        {
            var slots = await ReadPublishableSlotsAsync(game, cancellationToken);

            if (slots.Count == 0)
            {
                await modalService.Value.Show(ConfirmationDialogViewModel.Refusal(
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

            // Two endings, because the slot is in a different state in each and the sentence is the
            // only thing that says which. A publish that handed the save back emptied the folder.
            toasts.Show(outcome.KeptPlaying
                ? $"'{outcome.Savegame.Name}' is in {repo.Name}, and checked out to you. " +
                  "The save has not moved - check it in when you want somebody else to be able to take it."
                : $"'{outcome.Savegame.Name}' is in {repo.Name} and is anybody's to take. The local copy went to the " +
                  "Recycle Bin - check it out again once the game is on that mod list.");

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
    /// The slots holding bytes ModsDude has no copy of, which are the only ones a publish can be
    /// about.
    /// </summary>
    /// <remarks>
    /// An empty slot has nothing to publish, and a slot holding a checked-out save is checked in
    /// rather than published a second time under a new name - so both are absent from the picker
    /// rather than present and refused.
    /// </remarks>
    private async Task<IReadOnlyList<SavegameSlotOptionViewModel>> ReadPublishableSlotsAsync(
        Game game, CancellationToken cancellationToken)
    {
        var options = new List<SavegameSlotOptionViewModel>();

        foreach (var slot in await savegames.GetSlotsAsync(game, cancellationToken))
        {
            var availability = await savegames.ClassifySlotAsync(game, slot.Ref, cancellationToken);

            if (availability is SavegameSlotAvailability.Unrecognised)
            {
                options.Add(new SavegameSlotOptionViewModel(slot, availability));
            }
        }

        return options;
    }

    /// <summary>
    /// Makes a savegame out of what is already in a slot. Never a check-in: this one names the thing
    /// being created, and the two have opposite failure modes.
    /// </summary>
    /// <remarks>
    /// The profile is asked for here rather than taken from the game - see
    /// <see cref="SavegamePublishModalViewModel"/> - so this is also where the repo's profiles, the
    /// savegame each is currently following and what the mod folder is on are gathered.
    /// </remarks>
    /// <returns>What was created and whether it is still held, or null where the dialog was dismissed.</returns>
    private async Task<SavegamePublishOutcome?> PublishSlotAsync(
        Game game,
        Repo repo,
        SavegameSlotRef slot,
        string slotLabel,
        Guid? preselectProfileId,
        CancellationToken cancellationToken)
    {
        // This slot's own folder, because the first snapshot's revision is a declaration about the
        // mods that were beside these bytes: a save in the MP client's folder was played against the
        // MP client's mods, whatever the dedicated server is on.
        var manifest = manifestStore.TryRead(new ModTargetRef(game.Identity, slot.Target));
        var options = await BuildPublishOptionsAsync(repo, manifest?.ProfileId, manifest?.ProfileRevision, cancellationToken);

        // Which profile this game follows, which is what decides whether the save can be kept - a
        // publish to any other one hands it straight back. Null where the game follows nothing here,
        // and then only the no-mod-list answer may be kept.
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
            savegames.DescribeSlotNumber(game, slot));

        await modalService.Value.Show(modal);

        if (modal.Result is not string name)
        {
            return null;
        }

        var keepPlaying = modal.KeepPlaying;

        // Packing and uploading a save is minutes rather than seconds, and the page it was started
        // from is not where the user has to stay while it happens.
        using var task = backgroundTasks.Begin(
            $"Publishing '{name}' to {repo.Name}",
            keepPlaying
                ? $"Packing and uploading '{slotLabel}'"
                : $"Packing and uploading '{slotLabel}', then handing it back");

        task.DeclareTransfers(TransferDirection.Upload);

        var savegame = await savegames.PublishAsync(
            game, repo.Id, slot, name, modal.TrimmedLabel, modal.SelectedProfile?.ToTarget(), keepPlaying, cancellationToken,
            new SavegameStripProgress(task));

        return new SavegamePublishOutcome(savegame, keepPlaying);
    }

    /// <summary>
    /// Every profile in the repo as something the dialog can offer, plus the no-mod-list answer last.
    /// </summary>
    /// <remarks>
    /// <b>Archived savegames count towards "current".</b> Archiving is the repo-wide visibility state
    /// and deliberately does not release a profile's slot, so a profile whose current savegame is archived
    /// still has one - and a publish still supersedes it. Reading only the live list would leave that
    /// consequence unsaid.
    /// </remarks>
    private async Task<IReadOnlyList<SavegamePublishOption>> BuildPublishOptionsAsync(
        Repo repo,
        Guid? appliedProfileId,
        int? appliedRevision,
        CancellationToken cancellationToken)
    {
        // This dialog can be the first thing that needs them: the repo's Saves page is reachable
        // without ever having opened a profile.
        if (profileService.Profiles.Any(x => x.RepoId == repo.Id) is false)
        {
            await profileService.RefreshProfiles(repo.Id, cancellationToken);
        }

        var current = await ReadCurrentSavegamesAsync(repo.Id, cancellationToken);
        var options = new List<SavegamePublishOption>();

        foreach (var profile in profileService.Profiles.Where(x => x.RepoId == repo.Id).OrderBy(x => x.Name, NaturalOrder.Comparer))
        {
            var incumbent = current.GetValueOrDefault(profile.Id);

            options.Add(new SavegamePublishOption(
                profile.Id,
                profile.Name,
                SavegameService.DeclaredRevisionFor(profile.Id, profile.HeadRevision, appliedProfileId, appliedRevision),
                incumbent?.Name,
                incumbent?.Head?.ProfileRevision,
                profile.Id == appliedProfileId));
        }

        options.Add(SavegamePublishOption.NoModList);

        return options;
    }

    /// <summary>
    /// Which savegame each profile is following right now, keyed by profile.
    /// </summary>
    /// <remarks>
    /// A failed read costs the supersede notice and nothing else, which is the same bargain every
    /// other late-arriving fact in this feature strikes: the publish is still correct, the server
    /// still supersedes whatever is there, and the sentence saying so is simply absent rather than
    /// guessed at.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, SavegameDto>> ReadCurrentSavegamesAsync(Guid repoId, CancellationToken cancellationToken)
    {
        var current = new Dictionary<Guid, SavegameDto>();

        try
        {
            var savegames = await savegamesClient.GetSavegamesV1Async(repoId, cancellationToken);
            var archived = await savegamesClient.GetArchivedSavegamesV1Async(repoId, cancellationToken);

            foreach (var savegame in savegames.Concat(archived))
            {
                if (savegame.ProfileId is Guid profileId && savegame.SupersededAt is null)
                {
                    current[profileId] = savegame;
                }
            }
        }
        catch (ApiException)
        {
            return current;
        }

        return current;
    }


    /// <summary>
    /// Which mod list the snapshot about to be minted records, in the one line that says it.
    /// </summary>
    /// <remarks>
    /// <b>Read rather than recomputed.</b> The number is <see cref="ISavegameService.GetPlayedRevision"/>'s,
    /// which is the same one the check-in sends - working it out a second time here is how a dialog
    /// comes to name a revision the snapshot does not carry. The profile's name is this layer's to add:
    /// the binding records an id, and a bare "rev 1004" is a number belonging to no list in particular.
    /// </remarks>
    private string? DescribePlayedOn(Game game, Guid savegameId)
    {
        if (savegames.GetBinding(game, savegameId) is not SavegameCheckoutBinding binding
            || binding.ProfileId is not Guid profileId
            || savegames.GetPlayedRevision(game, savegameId) is not int revision)
        {
            return null;
        }

        var profile = profileService.Profiles.FirstOrDefault(x => x.Id == profileId);

        return profile is null
            ? $"Played on revision {revision} of its mod list."
            : $"Played on {profile.Name} rev {revision}.";
    }

    /// <summary>
    /// Somebody checked in while this save was out. Both answers are safe and neither destroys
    /// anything: a forced check-in becomes the head with the snapshot it was built on recorded beside
    /// it, so the fork ends up in the record rather than one side of it being lost.
    /// </summary>
    private async Task<SavegameCheckInOutcome> SendAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string? label,
        bool keepPlaying,
        bool force,
        CancellationToken cancellationToken,
        string? renameTo = null)
    {
        try
        {
            using var task = backgroundTasks.Begin($"Checking '{savegameName}' in", "Packing and uploading what is in the slot");
            task.DeclareTransfers(TransferDirection.Upload);

            var snapshot = await savegames.CheckInAsync(
                game, savegameId, label, keepPlaying, force, cancellationToken, new SavegameStripProgress(task), renameTo);

            return SavegameCheckInOutcome.CheckedIn(snapshot, keepPlaying);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.SavegameSnapshotStale)
        {
            var choice = new ConfirmationDialogViewModel(
                $"Somebody else checked '{savegameName}' in",
                "Your save was built on an older snapshot. Checking yours in anyway records it as the newest one, with " +
                "theirs named beside it and still in the history - nothing is deleted either way. Leaving it alone keeps " +
                "your copy exactly where it is, so you can look at theirs first and decide.",
                IconKind.Warning,
                "Check mine in anyway - theirs stays in the history",
                "Leave mine alone for now");

            await modalService.Value.Show(choice);

            if (choice.Result is false)
            {
                return SavegameCheckInOutcome.Deferred;
            }

            return await SendAsync(game, savegameId, savegameName, label, keepPlaying, force: true, cancellationToken, renameTo);
        }
        catch (UserFriendlyException exception)
        {
            await errorReporter.ShowAsync(exception, "checking a savegame in");

            return SavegameCheckInOutcome.Cancelled;
        }
    }
}


/// <summary>
/// The game a repo's savegames act on, with the two things their buttons turn on: what it is holding,
/// and which revision of which profile its mod folder was last made to match.
/// </summary>
/// <param name="UnreachableHolds">
/// The savegames held in a folder the settings no longer name, read once for the whole list. They
/// are still held and still claimed, and nothing that touches the bytes works on them - see
/// <see cref="ISavegameService.GetUnreachableHolds"/>.
/// </param>
public sealed record SavegameHost(
    Game Game,
    IReadOnlyList<SavegameCheckoutBinding> Held,
    IReadOnlySet<Guid> UnreachableHolds,
    Guid? AppliedProfileId,
    int? AppliedRevision);


/// <summary>
/// What a publish ended up doing: the savegame it made, and whether this machine still holds it.
/// </summary>
/// <remarks>
/// The second half is not decoration. A publish that handed the save back left an empty slot and a
/// savegame anybody can take, and a caller that said "checked out to you" over it would be describing
/// the state this dialog just took away.
/// </remarks>
public sealed record SavegamePublishOutcome(SavegameDto Savegame, bool KeptPlaying);


/// <summary>
/// What a check-in ended up doing. Three outcomes rather than a nullable snapshot, because "you backed
/// out" and "you chose to look at theirs first" leave the caller with different things to say.
/// </summary>
public sealed record SavegameCheckInOutcome(SavegameSnapshotDto? Snapshot, bool KeptPlaying, bool WasDeferred)
{
    public static SavegameCheckInOutcome Cancelled { get; } = new(null, false, false);

    /// <summary>The base was stale and the user chose to look at the newer snapshot first.</summary>
    public static SavegameCheckInOutcome Deferred { get; } = new(null, false, true);

    public static SavegameCheckInOutcome CheckedIn(SavegameSnapshotDto snapshot, bool keptPlaying)
        => new(snapshot, keptPlaying, false);

    public bool Succeeded => Snapshot is not null;

    /// <summary>Whether the slot is now free, which is what the caller has to re-read the disk about.</summary>
    public bool ReleasedTheSlot => Succeeded && KeptPlaying is false;
}
