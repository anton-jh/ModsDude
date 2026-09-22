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
/// The three verbs that act on a savegame already sitting in a slot - check in, discard and publish -
/// with the dialogs they need, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="ProfileApplyService"/>, and for the same reason: check-in is reached
/// from the repo's Saves list <em>and</em> from the check-out dialog's way out of a refused slot,
/// and two copies of "ask, send, resolve a stale base" would be two copies that eventually disagree
/// about what force means.
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
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks)
{
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
    /// Makes a savegame out of what is already in a slot. Never a check-in: this one names the thing
    /// being created, and the two have opposite failure modes.
    /// </summary>
    /// <remarks>
    /// The profile is asked for here rather than taken from the game - see
    /// <see cref="SavegamePublishModalViewModel"/> - so this is also where the repo's profiles, the
    /// savegame each is currently following and what the mod folder is on are gathered.
    /// </remarks>
    /// <returns>What was created and whether it is still held, or null where the dialog was dismissed.</returns>
    public async Task<SavegamePublishOutcome?> PublishAsync(
        Game game,
        Repo repo,
        SavegameSlotRef slot,
        string slotLabel,
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

        var modal = new SavegamePublishModalViewModel(
            slotLabel,
            repo.Name,
            slotLabel,
            options,
            options.FirstOrDefault(x => x.ProfileId == activeProfileId && x.ProfileId is not null),
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
