using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameFlowService
{
    /// <summary>
    /// Asks, uploads, and turns a refused base into a choice rather than an error.
    /// </summary>
    /// <param name="savegameName">What the modal calls the save. Display text only - see <paramref name="renameTo"/>.</param>
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
    Task<SavegameCheckInOutcome> CheckInAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        CancellationToken cancellationToken,
        string? renameTo = null);

    /// <summary>
    /// Hands the claim back without minting anything - taken by mistake, never played.
    /// </summary>
    /// <remarks>
    /// The confirmation carries what it costs, because this is the one verb with no snapshot behind it:
    /// whatever is in the slot is gone, and the Recycle Bin is the only way back.
    /// </remarks>
    Task<bool> DiscardAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        bool hasUnpublishedPlay,
        CancellationToken cancellationToken);

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
    /// <returns>False where the modal was dismissed, or there was nothing to forget.</returns>
    Task<bool> DisconnectAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        string? folderName);

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
    Task CheckInHeldAsync(
        Game game,
        Guid savegameId,
        string savegameName,
        Func<Task> changed,
        CancellationToken cancellationToken);

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
    SavegameHost? ReadHost(Repo repo);

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
    void Offer(Repo repo, SavegameListItemViewModel row, SavegameHost? host, Func<Guid, string?> nameOf);

    /// <summary>
    /// Whether any locked pin moved between two revisions. An unlocked mod at a different version is
    /// untidy; a locked map at a different version is a damaged save, and only the second is worth
    /// colouring a chip for.
    /// </summary>
    Task<bool> LockedPinMovedAsync(Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a savegame out, or takes a copy of it: the slot modal, then the claim, then the mods.
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
    /// savegame that was in the way - before the modal is offered again, so the caller's list is the
    /// one it names savegames from.
    /// </param>
    Task CheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Makes a savegame out of a save that is already on this disk: which slot, then the publish modal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The slot is what it is about</b>, so it is asked for first: the same flat slot list across
    /// every savegame folder the game reaches, filtered to the ones ModsDude has no copy of. Everything
    /// after that - the name, the mod list, the revision this first snapshot declares - is the publish
    /// modal's.
    /// </para>
    /// <para>
    /// Failures are reported here rather than thrown, because both callers reach this from a click and
    /// have nothing to add to the sentence.
    /// </para>
    /// </remarks>
    /// <param name="preselectProfileId">
    /// The profile the modal opens on, where the caller is a profile's own page. Null opens it on the
    /// profile this game follows - the modal still offers every answer either way.
    /// </param>
    /// <param name="published">Called with the new savegame's id once it exists, so the caller can re-read.</param>
    Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Func<Guid, Task> published,
        CancellationToken cancellationToken);
}
