using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// The savegame adapter for one game, for the callers that have a game rather than a repo.
/// </summary>
/// <remarks>
/// A seam for the same reason <see cref="IModFolders"/> and <see cref="IDriftCandidateSource"/>
/// are: hydrating an adapter needs the <em>repo's</em> base settings, which a game does not
/// carry, so the lookup goes through whichever repo on this machine serves the game's scope.
/// Behind an interface so the savegame engine depends on the one fact it uses and can be exercised
/// without a signed-in client.
/// </remarks>
public interface ILocalSavegameAdapters
{
    /// <returns>
    /// Null where no repo on this machine hydrates this game's adapter, or where the adapter has
    /// no savegame support at all. Both are ordinary states rather than errors - a game whose
    /// scope no loaded repo serves still exists, and a mods-only game has no slots by design.
    /// </returns>
    ILocalSavegameAdapter? TryGet(Game game);

    /// <inheritdoc cref="TryGet(Game)"/>
    /// <remarks>For callers that hold an identity rather than the game - the drift check, which walks
    /// <see cref="DriftCandidate"/>s.</remarks>
    ILocalSavegameAdapter? TryGet(GameIdentity identity);
}


/// <summary><see cref="ILocalSavegameAdapters"/> over the repos this client has loaded.</summary>
public sealed class RepoSavegameAdapters(RepoRepository repos, GameRepository games)
    : ILocalSavegameAdapters
{
    public ILocalSavegameAdapter? TryGet(GameIdentity identity)
        => games.Find(identity) is Game game
            ? TryGet(game)
            : null;

    public ILocalSavegameAdapter? TryGet(Game game)
    {
        // Any repo serving the identity will do. Two repos on the same game hydrate the same local
        // settings into the same slot list - the settings that differ between them are the mod
        // catalogue's, and a savegame adapter reads none of those.
        foreach (var repo in repos.Repos.Where(x => x.Scope == game.Identity))
        {
            if (repo.Adapter.CanSupportSavegames is false)
            {
                continue;
            }

            if (game.GetAdapter(repo.Adapter).GetLocalCapabilityAdapterFactory<ILocalSavegameAdapter>() is Func<ILocalSavegameAdapter> factory)
            {
                return factory();
            }
        }

        return null;
    }
}


/// <summary>
/// What the savegames a game is holding are to everything that is <em>not</em> the savegame
/// engine: a mod folder that may not be moved without the play in it being attributed first, one that
/// may not be moved at all, and a slot worth telling somebody about.
/// </summary>
/// <remarks>
/// A seam for the same reason <see cref="IModFolders"/> and <see cref="IProfileRevisions"/>
/// are: the sync engine and the drift monitor depend on the facts they actually use rather than on
/// the savegame client, and both can be exercised without a signed-in one. It is the whole of what
/// either of them knows about savegames.
/// </remarks>
public interface IHeldSavegames
{
    /// <summary>
    /// Looks at every slot <em>this target</em> is holding and, where the bytes have moved since the
    /// last look, records that the play happened on the revision its mod folder is on <em>now</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called before the manifest is rewritten, never after.</b> The manifest is what says which
    /// revision this folder runs, and it is the only thing that does - so once an apply has moved it,
    /// the evening that was played before the apply is indistinguishable from one played after, and
    /// the check-in would name the wrong mod list. See
    /// docs/10-savegame-profile-binding.md#play-attribution.
    /// </para>
    /// <para>
    /// <b>Per target rather than per game, because the bytes are in a folder.</b> A save in target
    /// T's savegame folder was played against target T's mod folder and nothing else, so applying to
    /// the dedicated server must not attribute the evening somebody played on the MP client to the
    /// revision the server just moved to. It is the same loop it always was, run once per folder the
    /// apply touches.
    /// </para>
    /// <para>
    /// Costs one hash per savegame held in that target, and a target holding none - which is nearly
    /// all of them, nearly all the time - costs one list read.
    /// </para>
    /// </remarks>
    Task ObserveAsync(ModTargetRef target, CancellationToken ct);

    /// <inheritdoc cref="SavegameHoldRules.RequiredRevision"/>
    int? GetRequiredRevision(GameIdentity game, Guid profileId);

    /// <inheritdoc cref="SavegameHoldRules.DecideApply"/>
    SavegameApplyDecision DecideApply(GameIdentity game, Guid profileId, int? revision);

    /// <inheritdoc cref="SavegameHoldRules.FindProfileHold"/>
    SavegameCheckoutBinding? FindProfileHold(GameIdentity game);

    /// <summary>
    /// Which of the held savegames have stopped agreeing with the server, for the drift notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the identity rather than the game because the drift monitor walks
    /// <see cref="DriftCandidate"/>s, which exist for games no loaded repo serves. One of those
    /// reports nothing, quietly.
    /// </para>
    /// <para>
    /// <b>Asked once for the game and answered per target.</b> The hold limit is the game's and the
    /// hashing is per held slot, so a call per folder would re-read the same list N times - but every
    /// answer is compared against <em>its own</em> target's manifest, and says which target it is
    /// about, so the notice can put it on the folder it belongs to. A hold whose target the adapter
    /// no longer offers is left out rather than reported: there is no folder to hash and nothing to
    /// compare, and the repo's Saves list is where that hold is said, on the savegame's own row.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(GameIdentity game, CancellationToken ct);
}


/// <summary>
/// Which mod list a savegame being published follows, and the revision its first snapshot declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>One value rather than two parameters</b>, because the two are all-or-nothing: the server carries
/// a check constraint saying so, and a half-set pair is the one invalid state
/// <see cref="SavegameSnapshotDto"/> has. Null in place of this record is the savegame that follows no
/// mod list - a real choice the publish dialog offers, not a fallback.
/// </para>
/// <para>
/// <b>The revision is declared, not observed</b>, and this is the only snapshot in the system of which
/// that is true. See <see cref="SavegameService.DeclaredRevisionFor"/> for which number it is.
/// </para>
/// </remarks>
public readonly record struct SavegamePublishTarget(Guid ProfileId, int Revision);


/// <summary>
/// The four verbs of a savegame - publish, check out, check in, discard - plus the slot questions the
/// picker asks before any of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>One holder at a time, an explicit hand-back, and a snapshot for every hand-back.</b> There is no
/// merge here and there never will be: two people's afternoons in one save cannot be reconciled by
/// anything, so the design refuses the situation instead of attempting the reconciliation. The
/// mechanical guarantee is the base-snapshot check at check-in; the checkout is the social half, and
/// only the first is a guarantee. See docs/PLAN.md#phase-8--savegames.
/// </para>
/// <para>
/// <b>Nothing here touches mods.</b> Checking a save out and applying the profile it needs are
/// offered to each other and are never steps inside each other - the caller sequences them, in the
/// order docs/PLAN.md#checking-out-in-order gives, because only the caller can ask the questions the
/// mod half needs asked.
/// </para>
/// </remarks>
public interface ISavegameService : IHeldSavegames
{
    /// <summary>
    /// Every slot this game has, occupied or not, in the order a picker should show them.
    /// </summary>
    /// <remarks>
    /// <b>One flat list across every savegame folder the game reaches</b>, each entry addressed and
    /// carrying what to call its folder where there is more than one. Flat because choosing where a
    /// save goes is choosing a place rather than a folder, and because a game with one target - which
    /// is nearly all of them - then reads exactly as it always did.
    /// </remarks>
    Task<IReadOnlyList<GameSavegameSlot>> GetSlotsAsync(Game game, CancellationToken ct);

    /// <summary>
    /// Which slot the picker should pre-select for this savegame: the slot it used last if that one
    /// is free, otherwise the first free slot, otherwise nothing.
    /// </summary>
    /// <remarks>
    /// <b>Advisory, and it never repairs anything.</b> The hint is returned by the store exactly as
    /// recorded however wrong it is, and finding out that the remembered slot is taken is this
    /// method's job at the moment it pre-selects - which is also the sentence the picker gets to say.
    /// Null means "no free slot", which is a real state: the rest can be full of saves ModsDude knows
    /// nothing about, and the answer there is the unrecognised-slot confirmation, not an eviction.
    /// </remarks>
    Task<SavegameSlotRef?> SuggestSlotAsync(Game game, Guid savegameId, CancellationToken ct);

    /// <summary>What one slot is, from the point of view of somebody about to write a savegame into it.</summary>
    /// <remarks>
    /// Hashes the slot only where a binding claims it, since that is the only case where the answer
    /// turns on the contents. An unrecognised slot is unrecognised whatever is in it.
    /// </remarks>
    Task<SavegameSlotAvailability> ClassifySlotAsync(Game game, SavegameSlotRef slot, CancellationToken ct);

    /// <summary>Takes the claim on a savegame and writes its head snapshot into a slot.</summary>
    /// <param name="progress">
    /// Where to say which stage the bytes are in and how far through it they are. The same
    /// parameter, with the same meaning, on every verb below that moves a save.
    /// </param>
    Task CheckOutAsync(Game game, SavegameDto savegame, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null);

    /// <summary>Writes a named snapshot into a slot without claiming anything.</summary>
    Task TakeCopyAsync(Game game, SavegameDto savegame, int snapshotNumber, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null);

    /// <summary>Hands a held savegame back, minting a snapshot from whatever is in its slot now.</summary>
    Task<SavegameSnapshotDto> CheckInAsync(Game game, Guid savegameId, string? label, bool keepPlaying, bool force, CancellationToken ct, IProgress<SavegameProgress>? progress = null);

    /// <summary>
    /// Puts a past savegame back in its profile's current slot, and lets go of the revision it was
    /// pinned to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of the swap publishing performs.</b> Whichever savegame held the slot becomes
    /// past in the same transaction - the server orders the two writes, because the instant where
    /// both are current is what the one-current-savegame index refuses.
    /// </para>
    /// <para>
    /// <b>The holder's pin goes with it.</b> A past savegame pins the mod folder to its own revision, and
    /// a savegame that is current again follows its profile - so a binding still naming a number would
    /// hold this game behind head forever and refuse every apply that tried to move it. That the
    /// hold is otherwise <em>decided once and does not move under the holder</em> is about somebody
    /// else's publish, which is not stated to whoever is playing; this is the opposite case, stated
    /// to the person doing it.
    /// </para>
    /// </remarks>
    /// <param name="games">
    /// Every installation that might be holding it, since the pin lives in local state per game
    /// and this verb is about a savegame rather than about a folder. Passing none is legitimate - a
    /// repo whose savegames nobody here has checked out.
    /// </param>
    Task<MakeSavegameCurrentResponse> MakeCurrentAsync(
        IReadOnlyList<Game> games,
        SavegameDto savegame,
        CancellationToken ct);

    /// <summary>Turns whatever is in a slot into a new savegame in the repo.</summary>
    /// <param name="target">
    /// Which mod list the new savegame follows and the revision its first snapshot declares, or null
    /// for a savegame that follows none. The pair travels as one value because the server refuses a
    /// half-set one, and because there is no third state - see
    /// docs/10-savegame-profile-binding.md#savegames-without-a-profile.
    /// </param>
    /// <param name="keepPlaying">
    /// Whether to stay holding the save afterwards. False hands it straight back, which is what
    /// publishing to a mod list this game is not on has to do - see <see cref="SavegameService.PublishAsync"/>.
    /// </param>
    Task<SavegameDto> PublishAsync(
        Game game,
        Guid repoId,
        SavegameSlotRef slot,
        string name,
        string? label,
        SavegamePublishTarget? target,
        bool keepPlaying,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null);

    /// <summary>Gives a savegame back without minting a snapshot - taken by mistake, never played.</summary>
    Task DiscardAsync(Game game, Guid savegameId, CancellationToken ct);

    /// <summary>
    /// Cuts every local tie to a savegame: this machine stops claiming to hold it, and stops
    /// remembering where it put it. The slot's contents are not touched and the server is not told.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The way out of a binding whose savegame is gone.</b> A save that was archived and then
    /// permanently deleted leaves a slot on this machine still reporting "checked out to you", against
    /// a savegame the server will not answer for - so check-in and discard both fail, and the row had
    /// no third option. This is that option, and it is the only one that can work: there is no claim
    /// left to release.
    /// </para>
    /// <para>
    /// <b>Also the way to keep a save and stop sharing it.</b> Same act from the other end - the
    /// folder becomes an ordinary save of the user's own, indistinguishable from one ModsDude never
    /// wrote, which is exactly what an unrecognised slot already is.
    /// </para>
    /// <para>
    /// <b>Local only, deliberately.</b> Where the savegame does still exist, this leaves the claim
    /// standing on the server - which is honest rather than convenient: releasing a claim is
    /// <see cref="DiscardAsync"/>, it is a thing other people are waiting on, and quietly doing it as
    /// a side effect of tidying local state would let somebody else take a save this machine has been
    /// playing.
    /// </para>
    /// </remarks>
    /// <returns>False where this game was holding no such savegame, which is idempotent rather than an error.</returns>
    bool Forget(Game game, Guid savegameId);

    /// <summary>Whether this game's adapter has savegames at all.</summary>
    bool SupportsSavegames(Game game);

    /// <summary>What this game holds for one savegame, or null where it holds none.</summary>
    SavegameCheckoutBinding? GetBinding(Game game, Guid savegameId);

    /// <summary>
    /// Which revision a check-in from here would record the play on, or null where it would record
    /// none.
    /// </summary>
    /// <remarks>
    /// The same answer <see cref="CheckInAsync"/> sends, read early so the check-in dialog can show
    /// it. That is the point of showing it at all: the attribution becomes visible at the moment it is
    /// recorded, while a wrong one can still be noticed. Null where the savegame follows no mod list,
    /// where nothing is held, and where no number could be found at all - the last of which
    /// <see cref="CheckInAsync"/> refuses rather than guesses, and which a dialog says nothing about.
    /// </remarks>
    int? GetPlayedRevision(Game game, Guid savegameId);

    /// <summary>
    /// What this game holds in one slot, or null. Null is not "the slot is empty" - it is the
    /// half of <see cref="SavegameSlotAvailability.Unrecognised"/> that says ModsDude did not put
    /// whatever is there. The picker reads it to offer "check that one in first" on a refused slot.
    /// </summary>
    SavegameCheckoutBinding? GetBindingForSlot(Game game, SavegameSlotRef slot);

    /// <summary>Everything this game currently holds. Short by construction.</summary>
    IReadOnlyList<SavegameCheckoutBinding> GetBindings(Game game);

    /// <summary>
    /// The savegames this game is holding in a target its adapter no longer offers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A hold this machine can no longer address, and the reason bindings are never swept.</b>
    /// Somebody emptied a folder field in the settings, or an adapter author renamed a key - which
    /// are the same event from here - and a savegame that was checked out into that folder is still
    /// checked out, still claimed on the server, and still sitting on this disk. There is no slot to
    /// list, nothing to hash and no folder to pack, so the only honest thing the app can do is say
    /// so where holds are shown, and let the hold come back if the field is filled in again.
    /// </para>
    /// <para>
    /// Empty where no repo on this machine hydrates the adapter: nothing can be said about targets
    /// nobody can enumerate, and reporting every hold as unreachable there would offer to forget
    /// savegames over a repo that simply is not loaded.
    /// </para>
    /// </remarks>
    IReadOnlyList<SavegameCheckoutBinding> GetUnreachableHolds(Game game);

    /// <summary>
    /// What to call the savegame folder a hold is in, or null where naming it would be noise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One rule in one place, because the answer differs between the two cases and both are said on
    /// the same list. A folder the game still reaches follows the rule every folder name in the app
    /// follows - <see cref="TargetNames.Distinguishing"/>, which is null for the single-folder game
    /// that nearly every game is, because a save is not in a folder <em>called</em> anything there.
    /// </para>
    /// <para>
    /// A folder it no longer reaches is always named, and by its key: no adapter can be asked what it
    /// was called, and the key is the only handle the user has on the thing they have to put back in
    /// the settings. That is the fallback half of <see cref="TargetNames"/>, and this is the second
    /// site written for it.
    /// </para>
    /// </remarks>
    /// <param name="target">The folder a hold is in, which need not be one the settings still name.</param>
    string? DescribeFolder(Game game, TargetKey target);

    /// <summary>
    /// The number the player knows a slot by, for a game that numbers them - or null. See
    /// <see cref="ILocalSavegameAdapter.GetSlotNumber"/>.
    /// </summary>
    int? DescribeSlotNumber(Game game, SavegameSlotRef slot);
}


/// <inheritdoc cref="ISavegameService"/>
public sealed class SavegameService(
    ISavegamesClient savegamesClient,
    IFilesClient filesClient,
    ISavegamePacker packer,
    SavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    IModFileDownloader downloader,
    IModFileUploader uploader,
    SyncManifestStore manifestStore,
    IRecycleBin recycleBin,
    ILogger<SavegameService> logger,
    ISavegameHeadSnapshots? headSnapshots = null)
    : ISavegameService
{
    private const int _bufferSize = 64 * 1024;

    /// <summary>One page is every snapshot any savegame is ever going to have; retention keeps ten.</summary>
    private const int _snapshotPageSize = 200;


    /// <summary>
    /// Whether an exception is the server refusing a check-in because somebody else checked in first.
    /// </summary>
    /// <remarks>
    /// <b>The one failure a caller has to tell apart from every other</b>, because it is not an error
    /// so much as a question: the head has moved, and forcing past it is a decision only the person
    /// holding the save can make. The original <see cref="ApiException{TResult}"/> is thrown
    /// unwrapped so a caller can read the server's own wording out of it - this is only here so a
    /// view model can ask the question without knowing the generated types.
    /// </remarks>
    public static bool IsSnapshotStale(Exception exception)
        => exception is ApiException<CustomProblemDetails> { Result.Type: ProblemType.SavegameSnapshotStale };


    public async Task<IReadOnlyList<GameSavegameSlot>> GetSlotsAsync(Game game, CancellationToken ct)
        => await ReadSlotsAsync(RequireAdapter(game), ct);

    public bool SupportsSavegames(Game game) => adapters.TryGet(game) is not null;

    public SavegameCheckoutBinding? GetBinding(Game game, Guid savegameId)
        => bindings.GetBinding(game.Identity, savegameId);

    public SavegameCheckoutBinding? GetBindingForSlot(Game game, SavegameSlotRef slot)
        => bindings.GetBindingForSlot(game.Identity, slot);

    public IReadOnlyList<SavegameCheckoutBinding> GetBindings(Game game)
        => bindings.GetBindings(game.Identity);

    public IReadOnlyList<SavegameCheckoutBinding> GetUnreachableHolds(Game game)
    {
        if (adapters.TryGet(game) is not ILocalSavegameAdapter adapter)
        {
            return [];
        }

        var targets = adapter.SavegameTargets;

        return [.. bindings.GetBindings(game.Identity).Where(x => targets[x.Slot.Target] is null)];
    }

    public string? DescribeFolder(Game game, TargetKey target)
    {
        // No adapter at all reads as "cannot be asked", which is the unreachable answer: the caller
        // is naming a folder a hold is in, and a hold nothing can address is exactly the case the key
        // is the only handle for.
        if (adapters.TryGet(game)?.SavegameTargets is not SavegameTargets targets)
        {
            return TargetNames.Of(target, null);
        }

        return targets[target] is SavegameTarget named
            ? TargetNames.Distinguishing(target, named.DisplayName, targets.Count)
            : TargetNames.Of(target, null);
    }

    /// <summary>
    /// The number a player knows a slot by, for a game that numbers them - see
    /// <see cref="ILocalSavegameAdapter.GetSlotNumber"/>. Null for one that does not, and for a game with
    /// no adapter to ask.
    /// </summary>
    public int? DescribeSlotNumber(Game game, SavegameSlotRef slot)
        => adapters.TryGet(game) is ILocalSavegameAdapter adapter ? adapter.GetSlotNumber(slot.Slot) : null;

    public int? GetPlayedRevision(Game game, Guid savegameId)
        => bindings.GetBinding(game.Identity, savegameId) is SavegameCheckoutBinding binding
            && binding.ProfileId is Guid profileId
            ? FindPlayedRevision(game, binding, profileId)
            : null;

    public int? GetRequiredRevision(GameIdentity game, Guid profileId)
        => SavegameHoldRules.RequiredRevision(bindings.GetBindings(game), profileId);

    public SavegameApplyDecision DecideApply(GameIdentity game, Guid profileId, int? revision)
        => SavegameHoldRules.DecideApply(bindings.GetBindings(game), profileId, revision);

    public SavegameCheckoutBinding? FindProfileHold(GameIdentity game)
        => SavegameHoldRules.FindProfileHold(bindings.GetBindings(game));

    /// <summary>
    /// Which revision of its profile a savegame runs on: <b>head for a current one, its own pinned
    /// revision for a past one</b>, and nothing for one that follows no mod list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static and public because the check-out dialog needs the answer for a savegame this machine is
    /// not holding yet, and it must be the same answer the binding will carry a moment later. Two
    /// computations of "which list does this savegame run on" is how a preview comes to describe a
    /// different apply from the one that runs.
    /// </para>
    /// <para>
    /// <b>The head snapshot's revision is not the answer for a current savegame.</b> It names the last
    /// list the savegame was <em>played</em> on, which is older than head whenever anybody has edited the
    /// profile since - and a current savegame follows its profile, which is what current means.
    /// Preparing the mod list before a session and then checking the savegame out is the ordinary case,
    /// and it must not be undone by the check-out.
    /// </para>
    /// </remarks>
    public static int? TargetRevisionOf(SavegameDto savegame)
        // Superseded implies a profile - the server refuses the other pairing - so this needs no
        // separate check for one.
        => savegame.SupersededAt is null ? null : savegame.Head?.ProfileRevision;

    /// <summary>
    /// Which revision a published savegame's first snapshot declares: <b>the revision the folder is
    /// actually on where that is a revision of the chosen profile</b>, and that profile's head
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Declared rather than observed, and nothing can change that.</b> The bytes predate ModsDude:
    /// there is no binding, no <see cref="SavegameCheckoutBinding.LastObservedHash"/> and no prior
    /// state, so nothing knows which mods were in the folder while that savegame was played. Requiring the
    /// chosen profile to be applied first would not recover it either - it would observe the folder at
    /// the moment of publishing, which is a different fact - so it is not required.
    /// </para>
    /// <para>
    /// Head is the honest answer for a profile the folder is <em>not</em> on, since the alternative is
    /// a number belonging to another mod list. The dialog shows whichever it is going to record, so
    /// the declaration is on screen rather than implied, and says out loud that nothing checks the
    /// savegame can run on it.
    /// </para>
    /// <para>
    /// Static and public for the same reason <see cref="TargetRevisionOf"/> is: the dialog needs the
    /// number before the publish runs, and two computations of it is how a dialog comes to show a
    /// different declaration from the one that is recorded.
    /// </para>
    /// </remarks>
    /// <param name="appliedProfileId">
    /// What the mod folder was last made to match, from the sync manifest, and
    /// <paramref name="appliedRevision"/> which revision of it. A folder that has never been synced
    /// has neither, and head is the answer.
    /// </param>
    public static int DeclaredRevisionFor(
        Guid profileId,
        int headRevision,
        Guid? appliedProfileId,
        int? appliedRevision)
        => profileId == appliedProfileId && appliedRevision is int applied ? applied : headRevision;

    public bool Forget(Game game, Guid savegameId)
    {
        // No adapter is required and none is asked for: this writes nothing to disk beyond local
        // state, which is what makes it work for a game whose scope no loaded repo serves.
        var forgotten = bindings.Forget(game.Identity, savegameId);

        if (forgotten)
        {
            logger.LogInformation(
                "Game {Game} stopped tracking savegame {Savegame}; the slot's contents were left alone.",
                game.Identity, savegameId);
        }

        return forgotten;
    }

    public async Task<SavegameSlotRef?> SuggestSlotAsync(Game game, Guid savegameId, CancellationToken ct)
    {
        var slots = await ReadSlotsAsync(RequireAdapter(game), ct);

        // Nothing is hashed here, and nothing needs to be: free-ness turns on the slot being empty
        // and unclaimed, and a hash can only ever tell two kinds of occupied apart. Reading a hint
        // must not cost twenty archive passes.
        bool IsFree(GameSavegameSlot slot)
            => SavegameSlotStates.Classify(slot, bindings.GetBindingForSlot(game.Identity, slot.Ref), null)
                is SavegameSlotAvailability.Free;

        // The whole reference, target included: the slot this save was last in is a place, and slot
        // 3 of the folder somebody has since repointed is not the same place as slot 3 of this one.
        if (bindings.GetSlotHint(game.Identity, savegameId) is SavegameSlotRef hint &&
            slots.FirstOrDefault(x => x.Ref.Addresses(hint)) is GameSavegameSlot remembered &&
            IsFree(remembered))
        {
            return remembered.Ref;
        }

        // The hint was wrong, or there was none. Either way the picker says so and offers this
        // instead; the hint itself is left exactly as it was, because it is about the next time.
        return slots.FirstOrDefault(IsFree)?.Ref;
    }

    public async Task<SavegameSlotAvailability> ClassifySlotAsync(Game game, SavegameSlotRef slot, CancellationToken ct)
    {
        var adapter = RequireAdapter(game);

        return await ClassifyAsync(game, adapter, RequireTarget(game, adapter, slot), slot, ct);
    }

    /// <summary>
    /// Takes the claim on a savegame and writes its head snapshot into a slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order is the design.</b> The destructive step is local and comes first, so a refusal
    /// costs nothing and nobody is left holding a save they were told they could not have. The claim
    /// is social and wants to be fast. Downloading is last because it is the slow part, and a user
    /// who wanders off during it still holds the save.
    /// </para>
    /// <para>
    /// <b>Mods are not this method's business.</b> Checking a save out never requires that anybody
    /// has thought about profiles, and applying the save's profile is the caller's step afterwards -
    /// with its own dialog where the mod folder holds things the repo does not know about. Folding it
    /// in here would make every check-out ask a mod question, which is the thing
    /// docs/PLAN.md#checking-out-in-order exists to prevent.
    /// </para>
    /// <para>
    /// <b>A failure after the claim leaves the claim taken and nothing bound, deliberately.</b> The
    /// alternative - releasing it on the way out - would let somebody else take a save whose bytes
    /// may already be on this disk, and a half-written slot with no binding is exactly the
    /// unrecognised state the safety check protects. Retrying the check-out is safe: taking a claim
    /// you already hold is not a conflict.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException">
    /// The slot holds play nobody has checked in, or this game already holds a savegame that
    /// claims its mod folder.
    /// </exception>
    public async Task CheckOutAsync(Game game, SavegameDto savegame, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null)
    {
        var adapter = RequireAdapter(game);
        var target = RequireTarget(game, adapter, slot);
        var head = savegame.Head
            ?? throw new UserFriendlyException(
                $"'{savegame.Name}' has nothing to check out",
                $"Savegame '{savegame.Id}' has no head snapshot, so there is nothing to write into a slot.");

        // Read off the snapshot rather than the savegame, because that is what the binding will record
        // a moment later and the limit has to count what the binding claims. The server keeps the two
        // in step - a snapshot's profile is its savegame's - so they cannot disagree.
        EnsureModFolderIsFree(game, savegame.Id, head.ProfileId, savegame.Name);

        await EnsureWritable(game, adapter, target, slot, savegame.Name, ct);

        await savegamesClient.CheckOutSavegameV1Async(savegame.RepoId, savegame.Id, ct);

        await DownloadIntoSlotAsync(adapter, target, savegame.RepoId, savegame.Id, head.ContentHash, slot.Slot, progress, ct);

        // Last, and only after the bytes are in place: this is the record that says the slot is ours
        // and which snapshot is in it, and writing it before the unpack would claim a slot holding
        // somebody else's save.
        bindings.SetBinding(game.Identity, new SavegameCheckoutBinding(
            savegame.RepoId,
            savegame.Id,
            slot,
            head.Number,
            head.ContentHash,
            DateTime.UtcNow)
        {
            ProfileId = head.ProfileId,
            ProfileRevision = head.ProfileRevision,
            // What the mod folder now has to be on, decided here because this is the last moment the
            // savegame's own current-or-past state is in hand. Everything afterwards - the apply, the
            // drift check - reads it back off the binding and never asks the server again.
            TargetRevision = TargetRevisionOf(savegame),
            // The bytes just written are the first observation, and nothing has been played on
            // anything yet - which is what makes a check-in that follows immediately record the
            // folder's own revision rather than inventing a session that never happened.
            LastObservedHash = head.ContentHash,
            LastPlayedRevision = null
        });
    }

    /// <summary>
    /// Writes a named snapshot into a slot with <b>no claim and no binding</b>. The slot is an
    /// ordinary unrecognised one afterwards.
    /// </summary>
    /// <remarks>
    /// What looking at an old snapshot without disturbing anybody looks like, and what a Guest gets:
    /// they may download and never check in. Nothing about it is reversible by ModsDude either -
    /// there is no snapshot to mint from it and no claim to give back, so it is a copy in the plainest
    /// sense.
    /// </remarks>
    public async Task TakeCopyAsync(Game game, SavegameDto savegame, int snapshotNumber, SavegameSlotRef slot, CancellationToken ct, IProgress<SavegameProgress>? progress = null)
    {
        var adapter = RequireAdapter(game);
        var target = RequireTarget(game, adapter, slot);

        await EnsureWritable(game, adapter, target, slot, savegame.Name, ct);

        var contentHash = await ResolveSnapshotHashAsync(savegame, snapshotNumber, ct);

        await DownloadIntoSlotAsync(adapter, target, savegame.RepoId, savegame.Id, contentHash, slot.Slot, progress, ct);

        // A binding that survived this would name a slot whose contents are now a different savegame
        // entirely, and the safety check would read that slot as unpublished play forever. The claim
        // it recorded is still open on the server - the caller is the one that can offer to give it
        // back, and it can only do that if this leaves a truthful local record behind.
        if (bindings.GetBindingForSlot(game.Identity, slot) is SavegameCheckoutBinding displaced)
        {
            bindings.ClearBinding(game.Identity, displaced.SavegameId);
        }
    }

    /// <summary>
    /// Hands a held savegame back, minting a snapshot from whatever is in its slot now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It asks nothing.</b> It acts on the slot the binding already names, because choosing
    /// between twenty near-identical folders from memory is precisely the moment where a wrong answer
    /// publishes somebody else's slot under this save's name and burns a snapshot doing it.
    /// </para>
    /// <para>
    /// <b>The local copy is recycled only after the commit.</b> Not after the upload - an uploaded
    /// blob that no snapshot names is unreachable - and never before. Every failure up to and
    /// including the commit leaves the binding and the folder exactly as they were, so the whole
    /// thing is retryable.
    /// </para>
    /// <para>
    /// <b>A stale base is not swallowed.</b> The server's <c>savegame-snapshot-stale</c> comes back
    /// out of here as the <see cref="ApiException{TResult}"/> it arrived as, carrying the server's own
    /// wording, so the caller can turn it into the "force?" question - see
    /// <see cref="IsSnapshotStale"/>. Forcing is a decision only the person holding the save can make,
    /// and it records the fork rather than hiding it.
    /// </para>
    /// </remarks>
    /// <param name="keepPlaying">
    /// Keeps the save checked out and the slot as it is, rebased onto the snapshot just minted. For
    /// somebody who wants tonight's progress on the server and intends to carry on.
    /// </param>
    /// <exception cref="UserFriendlyException">This machine holds no such savegame.</exception>
    public async Task<SavegameSnapshotDto> CheckInAsync(
        Game game,
        Guid savegameId,
        string? label,
        bool keepPlaying,
        bool force,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null)
    {
        var adapter = RequireAdapter(game);
        var binding = bindings.GetBinding(game.Identity, savegameId)
            ?? throw new UserFriendlyException(
                "This machine is not holding that savegame",
                $"No checkout binding for savegame '{savegameId}' in game '{game.Identity}'. Only the machine that checked a save out can check it in.");

        var target = RequireTarget(game, adapter, binding.Slot);
        var slot = binding.Slot.Slot;
        var packed = await packer.PackAsync(adapter, target, slot, ct, progress);

        // The last observation, and the packed hash is exactly what one would compute - the packer
        // hashes what it writes - so it costs no second pass over the folder. Play since the previous
        // look belongs to the revision this folder is on now, which is what the snapshot will name.
        binding = Observe(game.Identity, binding, packed.ContentHash);

        // Read from the slot these bytes came from, before the upload rather than after: the details
        // describe the snapshot being minted.
        var details = await DescribeAsync(adapter, target, slot, ct);

        SavegameSnapshotDto snapshot;

        try
        {
            await UploadAsync(binding.RepoId, savegameId, packed, progress, ct);

            progress?.Report(new SavegameProgress(SavegameStage.Recording, 0, 0));

            snapshot = await savegamesClient.CheckInSavegameV1Async(binding.RepoId, savegameId, new CheckInSavegameRequest
            {
                BasedOn = binding.Snapshot,
                ProfileRevision = ResolveAppliedRevision(game, binding),
                ContentHash = packed.ContentHash,
                SizeBytes = packed.SizeBytes,
                Label = label,
                Force = force,
                Details = details
            }, ct);
        }
        finally
        {
            // The archive is a temporary the packer handed us and nothing else will ever delete it.
            // The slot it was made from is untouched either way.
            TryDeleteFile(packed.FilePath);
        }

        if (keepPlaying)
        {
            // Rebased onto what was just minted, so the next check-in is based on this one rather
            // than on a snapshot that is no longer the head. The hash is the packed one and not the
            // slot's - they are the same bytes by construction, and re-hashing the folder would cost
            // a second full pass to learn nothing.
            bindings.SetBinding(game.Identity, binding with
            {
                Snapshot = snapshot.Number,
                ContentHash = snapshot.ContentHash,
                WrittenAt = DateTime.UtcNow,
                ProfileId = snapshot.ProfileId,
                ProfileRevision = snapshot.ProfileRevision,
                // Both boundaries move together, because this is a check-out in every respect that
                // matters: the snapshot on the server is these bytes, and the next evening is the
                // first that has not been recorded anywhere.
                LastObservedHash = snapshot.ContentHash,
                LastPlayedRevision = null
            });

            return snapshot;
        }

        // Only now. The binding goes first so that a failure to recycle cannot leave a slot claimed
        // by a savegame that is no longer checked out - the folder left behind reads as unrecognised,
        // which needs a confirmation to displace, and that is the safe way round.
        bindings.ClearBinding(game.Identity, savegameId);
        Recycle(adapter, target, slot);

        return snapshot;
    }

    /// <inheritdoc cref="ISavegameService.MakeCurrentAsync"/>
    public async Task<MakeSavegameCurrentResponse> MakeCurrentAsync(
        IReadOnlyList<Game> games,
        SavegameDto savegame,
        CancellationToken ct)
    {
        var response = await savegamesClient.MakeSavegameCurrentV1Async(savegame.RepoId, savegame.Id, ct);

        // After the server, and only after: a pin cleared against a swap that was then refused would
        // leave this game free to apply head under a savegame that is still past.
        foreach (var game in games)
        {
            if (bindings.GetBinding(game.Identity, savegame.Id) is not SavegameCheckoutBinding binding
                || binding.TargetRevision is null)
            {
                continue;
            }

            bindings.SetBinding(game.Identity, binding with { TargetRevision = null });

            logger.LogInformation(
                "Savegame {Savegame} is current again; game {Game} stopped pinning its mod folder to revision {Revision}.",
                savegame.Id, game.Identity, binding.TargetRevision);
        }

        return response;
    }

    /// <summary>
    /// Turns whatever is in a slot into a new savegame in the repo, and leaves this machine holding
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Publish is not check-in.</b> "Upload this new thing" and "upload a new snapshot of that
    /// thing" have opposite failure modes, and one button doing both is how the MVP managed to
    /// overwrite saves.
    /// </para>
    /// <para>
    /// <b>The id is minted here, not by the server.</b> The blob is addressed by
    /// <c>{repoId}/{savegameId}/{contentHash}</c>, so the bytes have to be uploadable before the
    /// savegame exists - which is the same order import uses, and the reason a publish that dies
    /// after the upload leaves an orphan blob for the reclamation sweep rather than a savegame whose
    /// head cannot be downloaded.
    /// </para>
    /// <para>
    /// <b>The profile is asked for rather than derived.</b> Every profile in the repo is a legitimate
    /// answer and so is none of them - the game's active one is only the likeliest - so the
    /// caller settles it and hands the pair down. The revision half is a <em>declaration</em>: the
    /// bytes predate ModsDude, nothing knows which mods were in the folder while that savegame was
    /// actually played, and no arrangement of this flow recovers it. Every snapshot after the first is
    /// observed.
    /// </para>
    /// <para>
    /// <b>Holding it afterwards is a choice, and one answer is not available.</b> Publishing to a mod
    /// list this game is not on would leave a savegame following one profile checked out into a folder
    /// on another - the exact state <see cref="SavegameDriftKind.PlayedOnAnotherModList"/> exists to
    /// report, arrived at in one gesture by somebody who did nothing wrong, and one no apply can clear
    /// because the apply table refuses every profile the folder could move to. The caller is what
    /// decides that; <c>SavegamePublishModalViewModel</c> is where the choice is offered and where it
    /// is not.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException">
    /// The game already holds a savegame that claims its mod folder.
    /// </exception>
    public async Task<SavegameDto> PublishAsync(
        Game game,
        Guid repoId,
        SavegameSlotRef slot,
        string name,
        string? label,
        SavegamePublishTarget? target,
        bool keepPlaying,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null)
    {
        var adapter = RequireAdapter(game);
        var savegameTarget = RequireTarget(game, adapter, slot);
        var savegameId = Guid.NewGuid();

        // The check-out limit reached from the other end rather than a rule of its own: a publish
        // opens a claim in the same transaction as the savegame, so it leaves this game holding
        // one - and only publishing *to a profile* claims the mod folder. One with no mod list has no
        // such precondition, which is why the id goes in nullable.
        EnsureModFolderIsFree(game, savegameId, target?.ProfileId, name);

        var packed = await packer.PackAsync(adapter, savegameTarget, slot.Slot, ct, progress);

        var details = await DescribeAsync(adapter, savegameTarget, slot.Slot, ct);

        SavegameDto savegame;

        try
        {
            await UploadAsync(repoId, savegameId, packed, progress, ct);

            progress?.Report(new SavegameProgress(SavegameStage.Recording, 0, 0));

            savegame = await savegamesClient.PublishSavegameV1Async(repoId, new PublishSavegameRequest
            {
                SavegameId = savegameId,
                Name = name,
                ProfileId = target?.ProfileId,
                ProfileRevision = target?.Revision,
                ContentHash = packed.ContentHash,
                SizeBytes = packed.SizeBytes,
                Label = label,
                Details = details
            }, ct);
        }
        finally
        {
            TryDeleteFile(packed.FilePath);
        }

        // Written even where the save is about to be handed straight back, because until the claim is
        // released this machine genuinely is holding it: the server opened one beside the snapshot.
        // Writing it unconditionally is what makes the hand-back below retryable - a release that
        // fails leaves a row offering Check in and Discard rather than a claim nothing on this machine
        // remembers taking.
        bindings.SetBinding(game.Identity, new SavegameCheckoutBinding(
            repoId,
            savegameId,
            slot,
            savegame.Head?.Number ?? 1,
            packed.ContentHash,
            DateTime.UtcNow)
        {
            ProfileId = target?.ProfileId,
            ProfileRevision = target?.Revision,
            // Null, not the revision just declared: publishing to a profile makes this its current
            // savegame - superseding whatever was - and a current savegame follows its profile from
            // then on rather than staying where it was published.
            TargetRevision = null,
            // The same clean slate a check-out leaves. The bytes in the slot are what was just
            // published, and whatever produced them happened before ModsDude saw this save at all -
            // which is why the first snapshot's revision is declared rather than observed.
            LastObservedHash = packed.ContentHash,
            LastPlayedRevision = null
        });

        if (keepPlaying is false)
        {
            // The same three steps a discard takes, in the same order and by the same route: release
            // the claim somebody else is waiting on, forget the binding, recycle the copy. Reused
            // rather than repeated - "publish and hand back" is a publish followed by exactly the
            // give-it-back verb, and two copies of that order would eventually disagree about it.
            await DiscardAsync(game, savegameId, ct);
        }

        return savegame;
    }

    /// <summary>
    /// Gives a savegame back without minting a snapshot, and recycles the local copy.
    /// </summary>
    /// <remarks>
    /// The way out of a checkout taken by mistake. Without it the only ways to release one are a junk
    /// snapshot nobody wanted and waiting to be taken over, and both of those are worse than an
    /// explicit "I never played this".
    /// </remarks>
    /// <exception cref="UserFriendlyException">This machine holds no such savegame.</exception>
    public async Task DiscardAsync(Game game, Guid savegameId, CancellationToken ct)
    {
        var adapter = RequireAdapter(game);
        var binding = bindings.GetBinding(game.Identity, savegameId)
            ?? throw new UserFriendlyException(
                "This machine is not holding that savegame",
                $"No checkout binding for savegame '{savegameId}' in game '{game.Identity}', so there is no claim of ours to give back.");

        // Before the server call, not after: discarding promises the local copy goes to the Recycle
        // Bin, and a hold whose folder the settings no longer name cannot keep that promise. Handing
        // the claim back and then failing would leave a save on this disk that nothing claims and
        // nothing can name - so this refuses instead, and Disconnect is the verb for that case.
        var target = RequireTarget(game, adapter, binding.Slot);

        // The server first: it is the half somebody else is waiting on, and a local record cleared
        // against a claim that is still open would leave the save unclaimable by anybody, this
        // machine included.
        await savegamesClient.DiscardSavegameCheckoutV1Async(binding.RepoId, savegameId, ct);

        bindings.ClearBinding(game.Identity, savegameId);
        Recycle(adapter, target, binding.Slot.Slot);
    }

    public async Task ObserveAsync(ModTargetRef target, CancellationToken ct)
    {
        var held = bindings.GetBindingsIn(target);

        // The overwhelmingly common answer, for one list read - the same bargain the drift check
        // strikes, and for the same reason: nearly every apply is to a folder holding nothing.
        if (held.Count == 0)
        {
            return;
        }

        // A game whose scope no loaded repo serves observes nothing, quietly - the same answer
        // the drift check gives for a folder it cannot reach.
        if (adapters.TryGet(target.Game) is not ILocalSavegameAdapter adapter)
        {
            return;
        }

        // The savegame folder paired with the mod folder being applied to. None means this target
        // holds mods and no saves, in which case nothing here is holding anything either - the
        // bindings above are keyed on the same key - so this is belt and braces for a settings edit
        // that landed between the two reads.
        if (adapter.SavegameTargets[target.Key] is not SavegameTarget savegameTarget)
        {
            return;
        }

        // Read once, before anything is hashed: it is this folder's own manifest, the same answer for
        // every binding in it, and it is the outgoing revision only until the caller rewrites it.
        var manifest = manifestStore.TryRead(target);
        var slots = await ReadSlotsOrNothing(adapter, savegameTarget, ct);

        foreach (var binding in held)
        {
            ct.ThrowIfCancellationRequested();

            // A savegame with no profile records nothing. It claims no mod list, so there is no
            // revision its play could belong to and nothing a check-in of it would send.
            if (binding.ProfileId is null)
            {
                continue;
            }

            var slot = slots.FirstOrDefault(x => x.Ref.Addresses(binding.Slot));

            // A slot somebody deleted from inside the game has no contents to have moved, and hashing
            // a missing folder would attribute the empty archive to this revision as an evening.
            if (slot?.IsOccupied is not true)
            {
                continue;
            }

            if (await HashOrNothing(adapter, savegameTarget, binding.Slot.Slot, ct) is string current)
            {
                Observe(target.Game, binding, current, manifest?.ProfileId, manifest?.ProfileRevision);
            }
        }
    }

    public async Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(GameIdentity game, CancellationToken ct)
    {
        var held = bindings.GetBindings(game);

        // The overwhelmingly common answer, and it costs one list read: a slot is occupied by
        // ModsDude only while a save is checked out, which is one or two saves, usually none.
        if (held.Count == 0)
        {
            return [];
        }

        // A game whose scope no loaded repo serves reports nothing. Unknown, not drifted - the
        // same answer the mod check gives for a folder it cannot reach.
        if (adapters.TryGet(game) is not ILocalSavegameAdapter adapter)
        {
            return [];
        }

        // One read per target that actually holds something, kept because a game's holds are usually
        // one or two in one folder and listing twenty slots twice for them is a directory pass with
        // nothing to show for it.
        var slotsByTarget = new Dictionary<TargetKey, IReadOnlyList<GameSavegameSlot>>();
        var drift = new List<SavegameDrift>();

        foreach (var binding in held)
        {
            ct.ThrowIfCancellationRequested();

            // A hold whose target the settings no longer name has no folder to look in, so there is
            // nothing here that could be compared against anything. It is not dropped and it is not
            // forgotten - the repo's Saves list is where an unreachable hold is said, on the
            // savegame's own row, because there is an action there and none here.
            if (adapter.SavegameTargets[binding.Slot.Target] is not SavegameTarget savegameTarget)
            {
                continue;
            }

            if (slotsByTarget.TryGetValue(binding.Slot.Target, out var slots) is false)
            {
                slots = await ReadSlotsOrNothing(adapter, savegameTarget, ct);
                slotsByTarget[binding.Slot.Target] = slots;
            }

            var slot = slots.FirstOrDefault(x => x.Ref.Addresses(binding.Slot));
            var head = headSnapshots?.GetHeadSnapshot(binding.RepoId, binding.SavegameId);

            // This folder's own manifest, not an average of the game's: what a held save was played
            // against is what the folder it sits in was applied to, and with several targets the
            // others are answering about somebody else's evening.
            var manifest = manifestStore.TryRead(new ModTargetRef(game, binding.Slot.Target));

            // One hash per held savegame, and only where the folder is still there. A slot the user
            // deleted from inside the game has no contents to have moved, and hashing a missing
            // folder would report the empty archive as unchecked-in play.
            var currentHash = slot?.IsOccupied is true
                ? await HashOrNothing(adapter, savegameTarget, binding.Slot.Slot, ct)
                : null;

            var kinds = SavegameDriftRules.Classify(
                binding,
                currentHash,
                head,
                manifest?.ProfileId,
                manifest?.ProfileRevision);

            drift.AddRange(kinds.Select(kind => new SavegameDrift(binding.RepoId, binding.SavegameId, binding.Slot, kind)
            {
                SlotDisplayName = slot?.DisplayName,
                HeldSnapshot = binding.Snapshot,
                HeadSnapshot = head,
                PlayedRevision = binding.ProfileRevision,
                AppliedRevision = manifest?.ProfileRevision,
                TargetRevision = binding.TargetRevision,
                // Computed here rather than reported by the rule, because the caller already holds
                // both halves and the rule answers kinds rather than reasons.
                RunsOnAnotherProfile = binding.ProfileId is not null
                    && manifest?.ProfileId is not null
                    && binding.ProfileId != manifest.ProfileId
            }));
        }

        return drift;
    }


    /// <summary>
    /// One observation: if the slot's bytes have moved since the last look, the play that moved them
    /// happened on the revision the folder is on now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole rule, and it is this short on purpose.</b> Attribution is by observation and not
    /// by timestamps: nothing here asks when anything happened, only whether the bytes are the ones
    /// last seen. Whether the folder has been synced at all is somebody else's guard - Check out is
    /// not offered until the profile has been applied, so a binding is only ever taken where a
    /// matching manifest already exists. See docs/10-savegame-profile-binding.md#the-procedure.
    /// </para>
    /// <para>
    /// Writes nothing when nothing moved. A binding rewritten on every apply would wake the drift
    /// notice for an answer that has not changed, and <see cref="SavegameBindingStore"/> saves the
    /// whole of local state per call.
    /// </para>
    /// <para>
    /// A folder with no revision this savegame can use leaves
    /// <see cref="SavegameCheckoutBinding.LastPlayedRevision"/> where it was rather than clearing it:
    /// there is nothing to attribute this evening to, and forgetting the list the last one ran on
    /// would be worse than not recording this one. The hash still moves, because the bytes still did.
    /// </para>
    /// </remarks>
    /// <param name="appliedProfileId">
    /// The profile the mod folder was last made to match. A revision of a <em>different</em> profile
    /// is not a number this savegame can record - revision 6 of two lists is one integer and two mod
    /// lists - and it would be refused by the server as not being the savegame's profile's.
    /// </param>
    /// <param name="appliedRevision">Which revision of it, from the manifest.</param>
    /// <returns>The binding as it now stands, so a caller holding one does not go on reading a stale copy.</returns>
    private SavegameCheckoutBinding Observe(
        GameIdentity game,
        SavegameCheckoutBinding binding,
        string currentContentHash,
        Guid? appliedProfileId,
        int? appliedRevision)
    {
        if (binding.ProfileId is not Guid profileId || ModContentHasher.Matches(currentContentHash, binding.LastObservedHash))
        {
            return binding;
        }

        // The folder's revision, where the folder is on this savegame's own mod list - see the
        // parameter. Anything else is a number this savegame cannot record.
        var played = profileId == appliedProfileId ? appliedRevision : null;

        var observed = binding with
        {
            LastObservedHash = currentContentHash,
            LastPlayedRevision = played ?? binding.LastPlayedRevision
        };

        bindings.SetBinding(game, observed);

        logger.LogInformation(
            "Savegame {Savegame} in game {Game} has been played since it was last looked at; attributed to profile revision {Revision}.",
            binding.SavegameId, game, observed.LastPlayedRevision);

        return observed;
    }

    /// <inheritdoc cref="Observe(GameIdentity, SavegameCheckoutBinding, string, Guid?, int?)"/>
    /// <remarks>Reads the manifest itself, for the caller that has not already.</remarks>
    private SavegameCheckoutBinding Observe(GameIdentity game, SavegameCheckoutBinding binding, string currentContentHash)
    {
        var manifest = ReadAppliedManifest(game, binding);

        return Observe(game, binding, currentContentHash, manifest?.ProfileId, manifest?.ProfileRevision);
    }

    /// <summary>
    /// Which mod list the folder this savegame sits in is on.
    /// </summary>
    /// <remarks>
    /// <b>A lookup, because the binding says which folder.</b> A manifest is per target and a held
    /// save was played against the mods in its own target's folder, so a game reaching three of them
    /// has one answer here rather than three - which is what the binding's target key bought. A
    /// target with no manifest has never been applied to, and null records nothing rather than
    /// guessing a mod list this save may never have run on.
    /// </remarks>
    private SyncManifest? ReadAppliedManifest(GameIdentity game, SavegameCheckoutBinding binding)
    {
        return manifestStore.TryRead(new ModTargetRef(game, binding.Slot.Target));
    }

    /// <summary>
    /// Refuses to take a second savegame that claims this game's mod folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The limit is about the folder, not about savegames.</b> One mod folder can only be on one
    /// revision, so two savegames following two mod lists cannot both be played out of one game - and
    /// that is the whole of the reason. A savegame with no profile makes no claim on the folder, so it
    /// neither counts nor is counted against; any number of those may be held at once.
    /// </para>
    /// <para>
    /// Refused here rather than only in the interface, because the interface is where it is
    /// <em>explained</em> and this is where it is true. The claim is taken on the server a moment
    /// later, and a claim taken for a check-out that then refuses itself is one somebody has to
    /// discard by hand.
    /// </para>
    /// </remarks>
    private void EnsureModFolderIsFree(Game game, Guid savegameId, Guid? profileId, string savegameName)
    {
        if (profileId is null)
        {
            return;
        }

        if (SavegameHoldRules.FindConflictingHold(bindings.GetBindings(game.Identity), savegameId) is not SavegameCheckoutBinding blocking)
        {
            return;
        }

        throw new UserFriendlyException(
            $"'{game.Name}' is already holding a savegame",
            $"Savegame '{blocking.SavegameId}' is checked out in game '{game.Identity}' and follows profile '{blocking.ProfileId}', so its mod folder is spoken for. Check that one in before taking '{savegameName}'.");
    }

    /// <summary>
    /// Refuses to write into a slot holding play nobody has checked in.
    /// </summary>
    /// <remarks>
    /// Only <see cref="SavegameSlotWriteDecision.Refused"/> stops anything here. A slot needing a
    /// confirmation has already had it by the time this runs - the picker is where that question
    /// belongs, because only it can name what the <em>game</em> calls the save that is about to be
    /// displaced.
    /// </remarks>
    private async Task EnsureWritable(
        Game game,
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotRef slot,
        string savegameName,
        CancellationToken ct)
    {
        var availability = await ClassifyAsync(game, adapter, target, slot, ct);

        if (SavegameSlotStates.IsRefused(availability) is false)
        {
            return;
        }

        throw new UserFriendlyException(
            "That slot holds play nobody has checked in",
            $"Writing '{savegameName}' into slot '{slot}' would destroy a savegame that has been played since it was checked out and exists nowhere else. Check that one in first.");
    }

    private async Task<SavegameSlotAvailability> ClassifyAsync(
        Game game,
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotRef slotRef,
        CancellationToken ct)
    {
        var slots = await adapter.GetSlots(target, ct);
        var slot = slots.FirstOrDefault(x => string.Equals(x.Id.Value, slotRef.Slot.Value, StringComparison.OrdinalIgnoreCase))
            // A slot the adapter does not list, for a game that can mint them. Nothing is there, so
            // there is nothing to lose - and a game that cannot mint them will refuse the write when
            // it comes to it, which is its call to make and not this one's.
            ?? new SavegameSlot(slotRef.Slot, null, false, [], adapter.GetSlotNumber(slotRef.Slot));

        var addressed = new GameSavegameSlot(new SavegameSlotRef(target.Key, slot.Id), target.DisplayName, slot);
        var binding = bindings.GetBindingForSlot(game.Identity, addressed.Ref);

        // Hashed only where something claims the slot: without a binding there is no recorded hash to
        // compare against, so the pass would cost a full archive read to change no answer.
        var currentHash = binding is not null && slot.IsOccupied
            ? await packer.HashSlotAsync(adapter, target, slot.Id, ct)
            : null;

        return SavegameSlotStates.Classify(addressed, binding, currentHash);
    }

    /// <summary>
    /// Fetches a snapshot's blob and replaces the slot's contents with it.
    /// </summary>
    /// <remarks>
    /// Staged to a temporary file rather than unpacked from the response stream, because a zip is
    /// read from its central directory at the end and a network stream cannot seek back. Verified
    /// against the hash the server addressed it by on the way past - one pass, no second read - since
    /// what lands here is about to replace somebody's slot.
    /// </remarks>
    private async Task DownloadIntoSlotAsync(
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        Guid repoId,
        Guid savegameId,
        string contentHash,
        SavegameSlotId slot,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct)
    {
        var link = await filesClient.CreateSavegameDownloadLinkV1Async(new CreateSavegameDownloadLinkRequest
        {
            RepoId = repoId,
            SavegameId = savegameId,
            ContentHash = contentHash
        }, ct);

        var archivePath = GetTemporaryArchivePath();

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        try
        {
            using (var download = await downloader.OpenAsync(link.Link, null, ct))
            await using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.Asynchronous))
            {
                // Counted as it is written to disk rather than through the downloader's own progress:
                // a ranged download reports what has arrived, which runs ahead of what the reader has
                // been handed, and this is the number the file will actually hold.
                var length = download.Length ?? 0;
                var received = 0L;

                progress?.Report(new SavegameProgress(SavegameStage.Downloading, 0, length));

                await ReportingCopy.CopyAsync(
                    download.Content,
                    file,
                    progress is null
                        ? null
                        : bytes => progress.Report(new SavegameProgress(SavegameStage.Downloading, received += bytes, length)),
                    ct);
            }

            await using (var written = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var size = written.Length;

                progress?.Report(new SavegameProgress(SavegameStage.Verifying, 0, size));

                var actual = await ModContentHasher.ComputeAsync(
                    written,
                    progress is null ? null : new InlineProgress<long>(read => progress.Report(new SavegameProgress(SavegameStage.Verifying, read, size))),
                    ct);

                if (ModContentHasher.Matches(actual, contentHash) is false)
                {
                    throw new UserFriendlyException(
                        "The savegame that arrived is not the one that was asked for",
                        $"Expected content hash '{contentHash}' but the downloaded blob hashes to '{actual}'. Nothing has been written into the slot.");
                }
            }

            await packer.UnpackAsync(archivePath, adapter, target, slot, ct, progress);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>
    /// Puts the packed bytes in storage, or establishes that they are already there.
    /// </summary>
    /// <remarks>
    /// <b>An <c>AlreadyStored</c> link skips the upload entirely</b>, which is the whole point of
    /// addressing the blob by its content: a check-in of a save somebody restored, or of bytes that
    /// went up once and were pruned back to, costs nothing at all. The mod uploader is reused as-is -
    /// staged blocks over a SAS with the hash stamped into metadata is the same mechanism whatever is
    /// in the file, and this is the mod upload path pointed at a different container.
    /// </remarks>
    private async Task UploadAsync(Guid repoId, Guid savegameId, PackedSavegame packed, IProgress<SavegameProgress>? progress, CancellationToken ct)
    {
        var link = await filesClient.CreateSavegameUploadLinkV1Async(new CreateSavegameUploadLinkRequest
        {
            RepoId = repoId,
            SavegameId = savegameId,
            ContentHash = packed.ContentHash
        }, ct);

        if (link.AlreadyStored)
        {
            return;
        }

        if (link.Link is not string destination)
        {
            throw new UserFriendlyException(
                "The server did not offer anywhere to upload the savegame",
                $"CreateSavegameUploadLink answered with neither a link nor alreadyStored for '{packed.ContentHash}'.");
        }

        progress?.Report(new SavegameProgress(SavegameStage.Uploading, 0, packed.SizeBytes));

        var uploaded = await uploader.UploadAsync(
            new ModFileUpload(destination, link.ContentHashMetadataKey, () => File.OpenRead(packed.FilePath))
            {
                BytesTransferred = progress is null
                    ? null
                    : new InlineProgress<long>(sent => progress.Report(new SavegameProgress(SavegameStage.Uploading, sent, packed.SizeBytes)))
            },
            ct);

        // The uploader hashes what it actually sent. Disagreeing with the packer means the archive
        // changed under us between the two reads, and committing a snapshot pointing at a blob nobody
        // can reproduce is worse than failing here - where nothing has been recorded yet.
        if (ModContentHasher.Matches(uploaded, packed.ContentHash) is false)
        {
            throw new UserFriendlyException(
                "The savegame changed while it was being uploaded",
                $"Packed as '{packed.ContentHash}' but uploaded '{uploaded}'. No snapshot has been recorded.");
        }
    }

    /// <summary>
    /// The content hash of one numbered snapshot.
    /// </summary>
    /// <remarks>
    /// The head is answered from what the caller already has, which is the overwhelmingly common case
    /// and saves a round trip; anything older costs the snapshot list, which is a page of rows and no
    /// blobs.
    /// </remarks>
    private async Task<string> ResolveSnapshotHashAsync(SavegameDto savegame, int snapshotNumber, CancellationToken ct)
    {
        if (savegame.Head is SavegameSnapshotDto head && head.Number == snapshotNumber)
        {
            return head.ContentHash;
        }

        var snapshots = await savegamesClient.GetSavegameSnapshotsV1Async(savegame.RepoId, savegame.Id, null, _snapshotPageSize, ct);

        return snapshots.Snapshots.FirstOrDefault(x => x.Number == snapshotNumber)?.ContentHash
            ?? throw new UserFriendlyException(
                $"Snapshot {snapshotNumber} of '{savegame.Name}' is not there any more",
                $"Savegame '{savegame.Id}' has no snapshot {snapshotNumber}. Retention keeps the last few snapshots and anything labelled; pruning leaves the gap where an old one was.");
    }

    /// <summary>
    /// Which revision the snapshot a check-in is about to mint was played on, or null where the
    /// savegame follows no mod list and there is no such thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What was observed beats what is installed.</b> The binding's
    /// <see cref="SavegameCheckoutBinding.LastPlayedRevision"/> is the revision the folder was on the
    /// last time the slot's bytes actually moved, which is the question a snapshot answers; the
    /// manifest only says what the folder runs at this instant, and an apply between the last evening
    /// and the hand-back moves it without anybody playing on it. Preferring the manifest would credit
    /// a fortnight-old session to a mod list it never ran on.
    /// </para>
    /// <para>
    /// The manifest is the fallback rather than the answer, and only where it describes the same
    /// profile the save was checked out against: two revision numbers belonging to two different
    /// profiles are not comparable, and the server rejects a revision that is not the savegame's
    /// profile's, so sending one because the user re-pointed the game would fail the check-in with
    /// a message about a profile they were not thinking about. What the binding recorded at check-out
    /// is the last resort, and it is still honest - it says which list the save was handed over on.
    /// </para>
    /// </remarks>
    private int? ResolveAppliedRevision(Game game, SavegameCheckoutBinding binding)
    {
        // A savegame following no mod list sends no revision at all, and the server refuses one that
        // does. Nothing was attributed to it either: Observe() leaves such a binding alone.
        if (binding.ProfileId is not Guid profileId)
        {
            return null;
        }

        return FindPlayedRevision(game, binding, profileId)
            ?? throw new UserFriendlyException(
                $"'{game.Name}' has no record of which mod list it is on",
                $"Neither the sync manifest for game '{game.Identity}' nor the checkout binding records a profile revision, and a savegame that follows a mod list has to name one. Apply the profile to this game and check in again.");
    }

    /// <summary>
    /// The three places the number can come from, in order. Split out from
    /// <see cref="ResolveAppliedRevision"/> so the check-in dialog can show the answer without
    /// inheriting its refusal: what it has to say is "played on rev 1004", and it has nothing useful
    /// to say about a savegame whose revision nothing on this machine knows.
    /// </summary>
    private int? FindPlayedRevision(Game game, SavegameCheckoutBinding binding, Guid profileId)
    {
        if (binding.LastPlayedRevision is int played)
        {
            return played;
        }

        var manifest = ReadAppliedManifest(game.Identity, binding);

        if (manifest?.ProfileRevision is int applied && profileId == manifest.ProfileId)
        {
            return applied;
        }

        return binding.ProfileRevision;
    }

    /// <summary>
    /// Sends a slot's folder to the Recycle Bin.
    /// </summary>
    /// <remarks>
    /// <b>A failure here is not an error.</b> The bytes are on the server by the time this runs, and
    /// the binding is already gone, so a folder left behind reads as an unrecognised slot - which
    /// needs a confirmation to displace and goes to the bin when it is. Deleting it outright instead
    /// would be the one thing the uninstall rules never permit, and failing the check-in over it
    /// would report a hand-back that plainly succeeded as broken.
    /// </remarks>
    private void Recycle(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot)
    {
        try
        {
            var path = adapter.GetSlotPath(target, slot);

            if (Directory.Exists(path))
            {
                recycleBin.TryRecycle(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The game holding a file open in a save that has just been checked in. Left where it is.
            logger.LogWarning(exception, "Could not clear slot {Slot} after checking in.", slot.Value);
        }
    }

    /// <summary>
    /// What the adapter says about the save in a slot, in the shape the server stores it - opaque,
    /// ordered, and read only to be displayed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read at the moment of publish or check-in, from the slot the bytes are being packed out of,
    /// so what is recorded describes the snapshot being minted rather than whatever that slot holds
    /// later.
    /// </para>
    /// <para>
    /// <b>Never allowed to fail the write.</b> A map name is decoration; the save is the thing. An
    /// adapter that throws, or a slot the game is holding open, costs the details and nothing else -
    /// same treatment mod imagery gets, and for the same reason.
    /// </para>
    /// </remarks>
    private async Task<List<SavegameDetailDto>> DescribeAsync(
        ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken ct)
    {
        try
        {
            var slots = await adapter.GetSlots(target, ct);

            return [.. slots
                .FirstOrDefault(x => x.Id == slot)?.Details
                    .Select(x => new SavegameDetailDto { Key = x.Id, Label = x.Label, Value = x.Value })
                ?? []];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not describe slot {Slot}; the snapshot will carry no details.", slot.Value);

            return [];
        }
    }

    private ILocalSavegameAdapter RequireAdapter(Game game)
        => adapters.TryGet(game)
            ?? throw new UserFriendlyException(
                $"'{game.Name}' has no savegames",
                $"No loaded repo hydrates a savegame adapter for game '{game.Identity}' - either its game does not support savegames, or no repo on this machine serves its scope.");

    /// <summary>
    /// The savegame folder a slot reference addresses.
    /// </summary>
    /// <remarks>
    /// <b>Where an unreachable hold is refused, and it has to be a refusal.</b> A settings field
    /// somebody emptied takes a target away while a savegame checked out into it is still on this
    /// disk; every verb that touches the bytes needs a folder, and the honest answer is to say which
    /// folder is missing rather than to pick another of the game's and write into it.
    /// </remarks>
    private static SavegameTarget RequireTarget(Game game, ILocalSavegameAdapter adapter, SavegameSlotRef slot)
        => adapter.SavegameTargets[slot.Target]
            ?? throw new UserFriendlyException(
                $"'{game.Name}' no longer has the folder that save is in",
                $"No savegame folder is configured for target '{slot.Target}' of game '{game.Identity}', so slot '{slot.Slot}' cannot be reached. Point the settings back at it, or disconnect the savegame to stop tracking it here.");

    /// <summary>
    /// Every slot of every savegame folder this game reaches, addressed and named.
    /// </summary>
    /// <remarks>
    /// Named by <see cref="TargetNames"/>, which is the one rule every folder name in the app
    /// follows: the adapter's own name where there is one, the key where there is not, and nothing
    /// at all where the game reaches a single savegame folder - a game with one does not have a
    /// savegame folder <em>called</em> something, and a picker grouping one group is a heading
    /// repeating the page title.
    /// </remarks>
    private async Task<IReadOnlyList<GameSavegameSlot>> ReadSlotsAsync(ILocalSavegameAdapter adapter, CancellationToken ct)
    {
        var targets = adapter.SavegameTargets;
        var slots = new List<GameSavegameSlot>();

        foreach (var target in targets)
        {
            var name = TargetNames.Distinguishing(target.Key, target.DisplayName, targets.Count);

            foreach (var slot in await adapter.GetSlots(target, ct))
            {
                slots.Add(new GameSavegameSlot(new SavegameSlotRef(target.Key, slot.Id), name, slot));
            }
        }

        return slots;
    }

    /// <summary>Slots, or nothing where the savegame folder is unreachable - unknown, never drifted.</summary>
    private async Task<IReadOnlyList<GameSavegameSlot>> ReadSlotsOrNothing(
        ILocalSavegameAdapter adapter, SavegameTarget target, CancellationToken ct)
    {
        try
        {
            return [.. (await adapter.GetSlots(target, ct))
                .Select(x => new GameSavegameSlot(new SavegameSlotRef(target.Key, x.Id), target.DisplayName, x))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreachable savegame folder reads as "no slots", which is deliberately
            // indistinguishable from an empty one to everything above - so this is the only place it
            // is visible.
            logger.LogWarning(exception, "Could not read the savegame slots in {Folder}; treating it as having none.", target.Path);

            return [];
        }
    }

    /// <summary>
    /// One slot's hash, or null where reading it failed. Null reports no drift rather than reporting
    /// play, for the reason <see cref="SavegameDriftRules"/> gives: a warning that fires when nothing
    /// is wrong is one everybody learns to click past.
    /// </summary>
    private async Task<string?> HashOrNothing(
        ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken ct)
    {
        try
        {
            return await packer.HashSlotAsync(adapter, target, slot, ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Reports no drift rather than play. Being wrong in this direction is quiet by design,
            // which is exactly why it has to be loud in the log.
            logger.LogWarning(exception, "Could not hash slot {Slot}; reporting no drift for it.", slot.Value);

            return null;
        }
    }

    private static string GetTemporaryArchivePath()
        => Path.Combine(Path.GetTempPath(), "modsdude", "savegames", $"{Guid.NewGuid():N}.zip");

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            // A leftover temporary archive costs disk space until the machine's temp folder is swept.
            logger.LogDebug(exception, "Could not delete the temporary archive {File}.", path);
        }
    }
}
