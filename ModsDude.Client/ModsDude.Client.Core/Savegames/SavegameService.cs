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
/// The savegame adapter for one instance, for the callers that have an instance rather than a repo.
/// </summary>
/// <remarks>
/// A seam for the same reason <see cref="IInstanceModFolders"/> and <see cref="IDriftCandidateSource"/>
/// are: hydrating an adapter needs the <em>repo's</em> base settings, which an instance does not
/// carry, so the lookup goes through whichever repo on this machine serves the instance's scope.
/// Behind an interface so the savegame engine depends on the one fact it uses and can be exercised
/// without a signed-in client.
/// </remarks>
public interface ILocalSavegameAdapters
{
    /// <returns>
    /// Null where no repo on this machine hydrates this instance's adapter, or where the adapter has
    /// no savegame support at all. Both are ordinary states rather than errors - an instance whose
    /// scope no loaded repo serves still exists, and a mods-only game has no slots by design.
    /// </returns>
    ILocalSavegameAdapter? TryGet(LocalInstance instance);

    /// <inheritdoc cref="TryGet(LocalInstance)"/>
    /// <remarks>For callers that hold an id rather than the instance - the drift check, which walks
    /// <see cref="DriftCandidate"/>s.</remarks>
    ILocalSavegameAdapter? TryGet(Guid instanceId);
}


/// <summary><see cref="ILocalSavegameAdapters"/> over the repos this client has loaded.</summary>
public sealed class RepoSavegameAdapters(RepoRepository repos, LocalInstanceRepository instances)
    : ILocalSavegameAdapters
{
    public ILocalSavegameAdapter? TryGet(Guid instanceId)
        => instances.Instances.FirstOrDefault(x => x.Id == instanceId) is LocalInstance instance
            ? TryGet(instance)
            : null;

    public ILocalSavegameAdapter? TryGet(LocalInstance instance)
    {
        // Any repo serving the scope will do. Two repos on the same game hydrate the same instance
        // settings into the same slot list - the settings that differ between them are the mod
        // catalogue's, and a savegame adapter reads none of those.
        foreach (var repo in repos.Repos.Where(x => x.Scope == instance.Scope))
        {
            if (repo.Adapter.CanSupportSavegames is false)
            {
                continue;
            }

            if (instance.GetAdapter(repo.Adapter).GetLocalCapabilityAdapterFactory<ILocalSavegameAdapter>() is Func<ILocalSavegameAdapter> factory)
            {
                return factory();
            }
        }

        return null;
    }
}


/// <summary>
/// What the savegames an instance is holding are to everything that is <em>not</em> the savegame
/// engine: a mod folder that may not be moved without the play in it being attributed first, one that
/// may not be moved at all, and a slot worth telling somebody about.
/// </summary>
/// <remarks>
/// A seam for the same reason <see cref="IInstanceModFolders"/> and <see cref="IProfileRevisions"/>
/// are: the sync engine and the drift monitor depend on the facts they actually use rather than on
/// the savegame client, and both can be exercised without a signed-in one. It is the whole of what
/// either of them knows about savegames.
/// </remarks>
public interface IHeldSavegames
{
    /// <summary>
    /// Looks at every slot this instance is holding and, where the bytes have moved since the last
    /// look, records that the play happened on the revision the mod folder is on <em>now</em>.
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
    /// Costs one hash per held savegame, and an instance holding none - which is nearly all of them,
    /// nearly all the time - costs one list read.
    /// </para>
    /// </remarks>
    Task ObserveAsync(Guid instanceId, CancellationToken ct);

    /// <inheritdoc cref="SavegameHoldRules.RequiredRevision"/>
    int? GetRequiredRevision(Guid instanceId, Guid profileId);

    /// <inheritdoc cref="SavegameHoldRules.DecideApply"/>
    SavegameApplyDecision DecideApply(Guid instanceId, Guid profileId, int? revision);

    /// <summary>
    /// Which of the held savegames have stopped agreeing with the server, for the drift notice.
    /// </summary>
    /// <remarks>
    /// Keyed on the id rather than the instance because the drift monitor walks
    /// <see cref="DriftCandidate"/>s, which exist for instances no loaded repo serves. One of those
    /// reports nothing, quietly.
    /// </remarks>
    Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(Guid instanceId, CancellationToken ct);
}


/// <summary>
/// Which mod list a savegame being published follows, and the revision its first version declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>One value rather than two parameters</b>, because the two are all-or-nothing: the server carries
/// a check constraint saying so, and a half-set pair is the one invalid state
/// <see cref="SavegameVersionDto"/> has. Null in place of this record is the savegame that follows no
/// mod list - a real choice the publish dialog offers, not a fallback.
/// </para>
/// <para>
/// <b>The revision is declared, not observed</b>, and this is the only version in the system of which
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
/// <b>One holder at a time, an explicit hand-back, and a version for every hand-back.</b> There is no
/// merge here and there never will be: two people's afternoons in one save cannot be reconciled by
/// anything, so the design refuses the situation instead of attempting the reconciliation. The
/// mechanical guarantee is the base-version check at check-in; the checkout is the social half, and
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
    /// <summary>Every slot this instance has, occupied or not, in the order a picker should show them.</summary>
    Task<IReadOnlyList<SavegameSlot>> GetSlotsAsync(LocalInstance instance, CancellationToken ct);

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
    Task<SavegameSlotId?> SuggestSlotAsync(LocalInstance instance, Guid savegameId, CancellationToken ct);

    /// <summary>What one slot is, from the point of view of somebody about to write a savegame into it.</summary>
    /// <remarks>
    /// Hashes the slot only where a binding claims it, since that is the only case where the answer
    /// turns on the contents. An unrecognised slot is unrecognised whatever is in it.
    /// </remarks>
    Task<SavegameSlotAvailability> ClassifySlotAsync(LocalInstance instance, SavegameSlotId slot, CancellationToken ct);

    /// <summary>Takes the claim on a savegame and writes its head version into a slot.</summary>
    Task CheckOutAsync(LocalInstance instance, SavegameDto savegame, SavegameSlotId slot, CancellationToken ct);

    /// <summary>Writes a named version into a slot without claiming anything.</summary>
    Task TakeCopyAsync(LocalInstance instance, SavegameDto savegame, int versionNumber, SavegameSlotId slot, CancellationToken ct);

    /// <summary>Hands a held savegame back, minting a version from whatever is in its slot now.</summary>
    Task<SavegameVersionDto> CheckInAsync(LocalInstance instance, Guid savegameId, string? label, bool keepPlaying, bool force, CancellationToken ct);

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
    /// hold this instance behind head forever and refuse every apply that tried to move it. That the
    /// hold is otherwise <em>decided once and does not move under the holder</em> is about somebody
    /// else's publish, which is not stated to whoever is playing; this is the opposite case, stated
    /// to the person doing it.
    /// </para>
    /// </remarks>
    /// <param name="instances">
    /// Every installation that might be holding it, since the pin lives in local state per instance
    /// and this verb is about a savegame rather than about a folder. Passing none is legitimate - a
    /// repo whose savegames nobody here has checked out.
    /// </param>
    Task<MakeSavegameCurrentResponse> MakeCurrentAsync(
        IReadOnlyList<LocalInstance> instances,
        SavegameDto savegame,
        CancellationToken ct);

    /// <summary>Turns whatever is in a slot into a new savegame in the repo.</summary>
    /// <param name="target">
    /// Which mod list the new savegame follows and the revision its first version declares, or null
    /// for a savegame that follows none. The pair travels as one value because the server refuses a
    /// half-set one, and because there is no third state - see
    /// docs/10-savegame-profile-binding.md#savegames-without-a-profile.
    /// </param>
    Task<SavegameDto> PublishAsync(
        LocalInstance instance,
        Guid repoId,
        SavegameSlotId slot,
        string name,
        string? label,
        SavegamePublishTarget? target,
        CancellationToken ct);

    /// <summary>Gives a savegame back without minting a version - taken by mistake, never played.</summary>
    Task DiscardAsync(LocalInstance instance, Guid savegameId, CancellationToken ct);

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
    /// <returns>False where this instance was holding no such savegame, which is idempotent rather than an error.</returns>
    bool Forget(LocalInstance instance, Guid savegameId);

    /// <summary>Whether this instance's adapter has savegames at all.</summary>
    bool SupportsSavegames(LocalInstance instance);

    /// <summary>What this instance holds for one savegame, or null where it holds none.</summary>
    SavegameCheckoutBinding? GetBinding(LocalInstance instance, Guid savegameId);

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
    int? GetPlayedRevision(LocalInstance instance, Guid savegameId);

    /// <summary>
    /// What this instance holds in one slot, or null. Null is not "the slot is empty" - it is the
    /// half of <see cref="SavegameSlotAvailability.Unrecognised"/> that says ModsDude did not put
    /// whatever is there. The picker reads it to offer "check that one in first" on a refused slot.
    /// </summary>
    SavegameCheckoutBinding? GetBindingForSlot(LocalInstance instance, SavegameSlotId slot);

    /// <summary>Everything this instance currently holds. Short by construction.</summary>
    IReadOnlyList<SavegameCheckoutBinding> GetBindings(LocalInstance instance);
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
    ISavegameHeadVersions? headVersions = null)
    : ISavegameService
{
    private const int _bufferSize = 64 * 1024;

    /// <summary>One page is every version any savegame is ever going to have; retention keeps ten.</summary>
    private const int _versionPageSize = 200;


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
    public static bool IsVersionStale(Exception exception)
        => exception is ApiException<CustomProblemDetails> { Result.Type: ProblemType.SavegameVersionStale };


    public async Task<IReadOnlyList<SavegameSlot>> GetSlotsAsync(LocalInstance instance, CancellationToken ct)
        => await RequireAdapter(instance).GetSlots(ct);

    public bool SupportsSavegames(LocalInstance instance) => adapters.TryGet(instance) is not null;

    public SavegameCheckoutBinding? GetBinding(LocalInstance instance, Guid savegameId)
        => bindings.GetBinding(instance.Id, savegameId);

    public SavegameCheckoutBinding? GetBindingForSlot(LocalInstance instance, SavegameSlotId slot)
        => bindings.GetBindingForSlot(instance.Id, slot);

    public IReadOnlyList<SavegameCheckoutBinding> GetBindings(LocalInstance instance)
        => bindings.GetBindings(instance.Id);

    public int? GetPlayedRevision(LocalInstance instance, Guid savegameId)
        => bindings.GetBinding(instance.Id, savegameId) is SavegameCheckoutBinding binding
            && binding.ProfileId is Guid profileId
            ? FindPlayedRevision(instance, binding, profileId)
            : null;

    public int? GetRequiredRevision(Guid instanceId, Guid profileId)
        => SavegameHoldRules.RequiredRevision(bindings.GetBindings(instanceId), profileId);

    public SavegameApplyDecision DecideApply(Guid instanceId, Guid profileId, int? revision)
        => SavegameHoldRules.DecideApply(bindings.GetBindings(instanceId), profileId, revision);

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
    /// <b>The head version's revision is not the answer for a current savegame.</b> It names the last
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
    /// Which revision a published savegame's first version declares: <b>the revision the folder is
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

    public bool Forget(LocalInstance instance, Guid savegameId)
    {
        // No adapter is required and none is asked for: this writes nothing to disk beyond local
        // state, which is what makes it work for an instance whose scope no loaded repo serves.
        var forgotten = bindings.Forget(instance.Id, savegameId);

        if (forgotten)
        {
            logger.LogInformation(
                "Instance {Instance} stopped tracking savegame {Savegame}; the slot's contents were left alone.",
                instance.Id, savegameId);
        }

        return forgotten;
    }

    public async Task<SavegameSlotId?> SuggestSlotAsync(LocalInstance instance, Guid savegameId, CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);
        var slots = await adapter.GetSlots(ct);

        // Nothing is hashed here, and nothing needs to be: free-ness turns on the slot being empty
        // and unclaimed, and a hash can only ever tell two kinds of occupied apart. Reading a hint
        // must not cost twenty archive passes.
        bool IsFree(SavegameSlot slot)
            => SavegameSlotStates.Classify(slot, bindings.GetBindingForSlot(instance.Id, slot.Id), null)
                is SavegameSlotAvailability.Free;

        if (bindings.GetSlotHint(instance.Id, savegameId) is string hint &&
            slots.FirstOrDefault(x => string.Equals(x.Id.Value, hint, StringComparison.OrdinalIgnoreCase)) is SavegameSlot remembered &&
            IsFree(remembered))
        {
            return remembered.Id;
        }

        // The hint was wrong, or there was none. Either way the picker says so and offers this
        // instead; the hint itself is left exactly as it was, because it is about the next time.
        return slots.FirstOrDefault(IsFree)?.Id;
    }

    public async Task<SavegameSlotAvailability> ClassifySlotAsync(LocalInstance instance, SavegameSlotId slot, CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);

        return await ClassifyAsync(instance, adapter, slot, ct);
    }

    /// <summary>
    /// Takes the claim on a savegame and writes its head version into a slot.
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
    /// The slot holds play nobody has checked in, or this instance already holds a savegame that
    /// claims its mod folder.
    /// </exception>
    public async Task CheckOutAsync(LocalInstance instance, SavegameDto savegame, SavegameSlotId slot, CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);
        var head = savegame.Head
            ?? throw new UserFriendlyException(
                $"'{savegame.Name}' has nothing to check out",
                $"Savegame '{savegame.Id}' has no head version, so there is nothing to write into a slot.");

        // Read off the version rather than the savegame, because that is what the binding will record
        // a moment later and the limit has to count what the binding claims. The server keeps the two
        // in step - a version's profile is its savegame's - so they cannot disagree.
        EnsureModFolderIsFree(instance, savegame.Id, head.ProfileId, savegame.Name);

        await EnsureWritable(instance, adapter, slot, savegame.Name, ct);

        await savegamesClient.CheckOutSavegameV1Async(savegame.RepoId, savegame.Id, ct);

        await DownloadIntoSlotAsync(adapter, savegame.RepoId, savegame.Id, head.ContentHash, slot, ct);

        // Last, and only after the bytes are in place: this is the record that says the slot is ours
        // and which version is in it, and writing it before the unpack would claim a slot holding
        // somebody else's save.
        bindings.SetBinding(instance.Id, new SavegameCheckoutBinding(
            savegame.RepoId,
            savegame.Id,
            slot.Value,
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
    /// Writes a named version into a slot with <b>no claim and no binding</b>. The slot is an
    /// ordinary unrecognised one afterwards.
    /// </summary>
    /// <remarks>
    /// What looking at an old version without disturbing anybody looks like, and what a Guest gets:
    /// they may download and never check in. Nothing about it is reversible by ModsDude either -
    /// there is no version to mint from it and no claim to give back, so it is a copy in the plainest
    /// sense.
    /// </remarks>
    public async Task TakeCopyAsync(LocalInstance instance, SavegameDto savegame, int versionNumber, SavegameSlotId slot, CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);

        await EnsureWritable(instance, adapter, slot, savegame.Name, ct);

        var contentHash = await ResolveVersionHashAsync(savegame, versionNumber, ct);

        await DownloadIntoSlotAsync(adapter, savegame.RepoId, savegame.Id, contentHash, slot, ct);

        // A binding that survived this would name a slot whose contents are now a different savegame
        // entirely, and the safety check would read that slot as unpublished play forever. The claim
        // it recorded is still open on the server - the caller is the one that can offer to give it
        // back, and it can only do that if this leaves a truthful local record behind.
        if (bindings.GetBindingForSlot(instance.Id, slot) is SavegameCheckoutBinding displaced)
        {
            bindings.ClearBinding(instance.Id, displaced.SavegameId);
        }
    }

    /// <summary>
    /// Hands a held savegame back, minting a version from whatever is in its slot now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It asks nothing.</b> It acts on the slot the binding already names, because choosing
    /// between twenty near-identical folders from memory is precisely the moment where a wrong answer
    /// publishes somebody else's slot under this save's name and burns a version doing it.
    /// </para>
    /// <para>
    /// <b>The local copy is recycled only after the commit.</b> Not after the upload - an uploaded
    /// blob that no version names is unreachable - and never before. Every failure up to and
    /// including the commit leaves the binding and the folder exactly as they were, so the whole
    /// thing is retryable.
    /// </para>
    /// <para>
    /// <b>A stale base is not swallowed.</b> The server's <c>savegame-version-stale</c> comes back
    /// out of here as the <see cref="ApiException{TResult}"/> it arrived as, carrying the server's own
    /// wording, so the caller can turn it into the "force?" question - see
    /// <see cref="IsVersionStale"/>. Forcing is a decision only the person holding the save can make,
    /// and it records the fork rather than hiding it.
    /// </para>
    /// </remarks>
    /// <param name="keepPlaying">
    /// Keeps the save checked out and the slot as it is, rebased onto the version just minted. For
    /// somebody who wants tonight's progress on the server and intends to carry on.
    /// </param>
    /// <exception cref="UserFriendlyException">This machine holds no such savegame.</exception>
    public async Task<SavegameVersionDto> CheckInAsync(
        LocalInstance instance,
        Guid savegameId,
        string? label,
        bool keepPlaying,
        bool force,
        CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);
        var binding = bindings.GetBinding(instance.Id, savegameId)
            ?? throw new UserFriendlyException(
                "This machine is not holding that savegame",
                $"No checkout binding for savegame '{savegameId}' in instance '{instance.Id}'. Only the machine that checked a save out can check it in.");

        var slot = new SavegameSlotId(binding.SlotId);
        var packed = await packer.PackAsync(adapter, slot, ct);

        // The last observation, and the packed hash is exactly what one would compute - the packer
        // hashes what it writes - so it costs no second pass over the folder. Play since the previous
        // look belongs to the revision this folder is on now, which is what the version will name.
        binding = Observe(instance.Id, binding, packed.ContentHash);

        // Read from the slot these bytes came from, before the upload rather than after: the details
        // describe the version being minted.
        var details = await DescribeAsync(adapter, slot, ct);

        SavegameVersionDto version;

        try
        {
            await UploadAsync(binding.RepoId, savegameId, packed, ct);

            version = await savegamesClient.CheckInSavegameV1Async(binding.RepoId, savegameId, new CheckInSavegameRequest
            {
                BasedOn = binding.Version,
                ProfileRevision = ResolveAppliedRevision(instance, binding),
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
            // than on a version that is no longer the head. The hash is the packed one and not the
            // slot's - they are the same bytes by construction, and re-hashing the folder would cost
            // a second full pass to learn nothing.
            bindings.SetBinding(instance.Id, binding with
            {
                Version = version.Number,
                ContentHash = version.ContentHash,
                WrittenAt = DateTime.UtcNow,
                ProfileId = version.ProfileId,
                ProfileRevision = version.ProfileRevision,
                // Both boundaries move together, because this is a check-out in every respect that
                // matters: the version on the server is these bytes, and the next evening is the
                // first that has not been recorded anywhere.
                LastObservedHash = version.ContentHash,
                LastPlayedRevision = null
            });

            return version;
        }

        // Only now. The binding goes first so that a failure to recycle cannot leave a slot claimed
        // by a savegame that is no longer checked out - the folder left behind reads as unrecognised,
        // which needs a confirmation to displace, and that is the safe way round.
        bindings.ClearBinding(instance.Id, savegameId);
        Recycle(adapter, slot);

        return version;
    }

    /// <inheritdoc cref="ISavegameService.MakeCurrentAsync"/>
    public async Task<MakeSavegameCurrentResponse> MakeCurrentAsync(
        IReadOnlyList<LocalInstance> instances,
        SavegameDto savegame,
        CancellationToken ct)
    {
        var response = await savegamesClient.MakeSavegameCurrentV1Async(savegame.RepoId, savegame.Id, ct);

        // After the server, and only after: a pin cleared against a swap that was then refused would
        // leave this instance free to apply head under a savegame that is still past.
        foreach (var instance in instances)
        {
            if (bindings.GetBinding(instance.Id, savegame.Id) is not SavegameCheckoutBinding binding
                || binding.TargetRevision is null)
            {
                continue;
            }

            bindings.SetBinding(instance.Id, binding with { TargetRevision = null });

            logger.LogInformation(
                "Savegame {Savegame} is current again; instance {Instance} stopped pinning its mod folder to revision {Revision}.",
                savegame.Id, instance.Id, binding.TargetRevision);
        }

        return response;
    }

    /// <summary>
    /// Turns whatever is in a slot into a new savegame in the repo, and leaves this machine holding
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Publish is not check-in.</b> "Upload this new thing" and "upload a new version of that
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
    /// answer and so is none of them - the instance's active one is only the likeliest - so the
    /// caller settles it and hands the pair down. The revision half is a <em>declaration</em>: the
    /// bytes predate ModsDude, nothing knows which mods were in the folder while that savegame was
    /// actually played, and no arrangement of this flow recovers it. Every version after the first is
    /// observed.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException">
    /// The instance already holds a savegame that claims its mod folder.
    /// </exception>
    public async Task<SavegameDto> PublishAsync(
        LocalInstance instance,
        Guid repoId,
        SavegameSlotId slot,
        string name,
        string? label,
        SavegamePublishTarget? target,
        CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);
        var savegameId = Guid.NewGuid();

        // The check-out limit reached from the other end rather than a rule of its own: a publish
        // opens a claim in the same transaction as the savegame, so it leaves this instance holding
        // one - and only publishing *to a profile* claims the mod folder. One with no mod list has no
        // such precondition, which is why the id goes in nullable.
        EnsureModFolderIsFree(instance, savegameId, target?.ProfileId, name);

        var packed = await packer.PackAsync(adapter, slot, ct);

        var details = await DescribeAsync(adapter, slot, ct);

        SavegameDto savegame;

        try
        {
            await UploadAsync(repoId, savegameId, packed, ct);

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

        // Publishing leaves you holding it: the server opens a claim beside the version, and this is
        // the local half of the same fact. Without it the slot the save is sitting in would read as
        // unrecognised the moment it was published.
        bindings.SetBinding(instance.Id, new SavegameCheckoutBinding(
            repoId,
            savegameId,
            slot.Value,
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
            // which is why the first version's revision is declared rather than observed.
            LastObservedHash = packed.ContentHash,
            LastPlayedRevision = null
        });

        return savegame;
    }

    /// <summary>
    /// Gives a savegame back without minting a version, and recycles the local copy.
    /// </summary>
    /// <remarks>
    /// The way out of a checkout taken by mistake. Without it the only ways to release one are a junk
    /// version nobody wanted and waiting to be taken over, and both of those are worse than an
    /// explicit "I never played this".
    /// </remarks>
    /// <exception cref="UserFriendlyException">This machine holds no such savegame.</exception>
    public async Task DiscardAsync(LocalInstance instance, Guid savegameId, CancellationToken ct)
    {
        var adapter = RequireAdapter(instance);
        var binding = bindings.GetBinding(instance.Id, savegameId)
            ?? throw new UserFriendlyException(
                "This machine is not holding that savegame",
                $"No checkout binding for savegame '{savegameId}' in instance '{instance.Id}', so there is no claim of ours to give back.");

        // The server first: it is the half somebody else is waiting on, and a local record cleared
        // against a claim that is still open would leave the save unclaimable by anybody, this
        // machine included.
        await savegamesClient.DiscardSavegameCheckoutV1Async(binding.RepoId, savegameId, ct);

        bindings.ClearBinding(instance.Id, savegameId);
        Recycle(adapter, new SavegameSlotId(binding.SlotId));
    }

    public async Task ObserveAsync(Guid instanceId, CancellationToken ct)
    {
        var held = bindings.GetBindings(instanceId);

        // The overwhelmingly common answer, for one list read - the same bargain the drift check
        // strikes, and for the same reason: nearly every apply is to an instance holding nothing.
        if (held.Count == 0)
        {
            return;
        }

        // An instance whose scope no loaded repo serves observes nothing, quietly - the same answer
        // the drift check gives for a folder it cannot reach.
        var adapter = adapters.TryGet(instanceId);

        if (adapter is null)
        {
            return;
        }

        // Read once, before anything is hashed: it is the same folder for every binding, and it is
        // the outgoing revision only until the caller rewrites it.
        var manifest = manifestStore.TryRead(instanceId);
        var slots = await ReadSlotsOrNothing(adapter, ct);

        foreach (var binding in held)
        {
            ct.ThrowIfCancellationRequested();

            // A savegame with no profile records nothing. It claims no mod list, so there is no
            // revision its play could belong to and nothing a check-in of it would send.
            if (binding.ProfileId is null)
            {
                continue;
            }

            var slot = slots.FirstOrDefault(x => string.Equals(x.Id.Value, binding.SlotId, StringComparison.OrdinalIgnoreCase));

            // A slot somebody deleted from inside the game has no contents to have moved, and hashing
            // a missing folder would attribute the empty archive to this revision as an evening.
            if (slot?.IsOccupied is not true)
            {
                continue;
            }

            if (await HashOrNothing(adapter, new SavegameSlotId(binding.SlotId), ct) is string current)
            {
                Observe(instanceId, binding, current, manifest?.ProfileId, manifest?.ProfileRevision);
            }
        }
    }

    public async Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(Guid instanceId, CancellationToken ct)
    {
        var held = bindings.GetBindings(instanceId);

        // The overwhelmingly common answer, and it costs one list read: a slot is occupied by
        // ModsDude only while a save is checked out, which is one or two saves, usually none.
        if (held.Count == 0)
        {
            return [];
        }

        // An instance whose scope no loaded repo serves reports nothing. Unknown, not drifted - the
        // same answer the mod check gives for a folder it cannot reach.
        var adapter = Maybe.From(adapters.TryGet(instanceId));

        if (adapter.HasValue is false)
        {
            return [];
        }

        var manifest = manifestStore.TryRead(instanceId);
        var slots = await ReadSlotsOrNothing(adapter.Value, ct);
        var drift = new List<SavegameDrift>();

        foreach (var binding in held)
        {
            ct.ThrowIfCancellationRequested();

            var slotId = new SavegameSlotId(binding.SlotId);
            var slot = slots.FirstOrDefault(x => string.Equals(x.Id.Value, binding.SlotId, StringComparison.OrdinalIgnoreCase));
            var head = headVersions?.GetHeadVersion(binding.RepoId, binding.SavegameId);

            // One hash per held savegame, and only where the folder is still there. A slot the user
            // deleted from inside the game has no contents to have moved, and hashing a missing
            // folder would report the empty archive as unchecked-in play.
            var currentHash = slot?.IsOccupied is true
                ? await HashOrNothing(adapter.Value, slotId, ct)
                : null;

            var kinds = SavegameDriftRules.Classify(
                binding,
                currentHash,
                head,
                manifest?.ProfileId,
                manifest?.ProfileRevision);

            drift.AddRange(kinds.Select(kind => new SavegameDrift(binding.RepoId, binding.SavegameId, slotId, kind)
            {
                SlotDisplayName = slot?.DisplayName,
                HeldVersion = binding.Version,
                HeadVersion = head,
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
        Guid instanceId,
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

        bindings.SetBinding(instanceId, observed);

        logger.LogInformation(
            "Savegame {Savegame} in instance {Instance} has been played since it was last looked at; attributed to profile revision {Revision}.",
            binding.SavegameId, instanceId, observed.LastPlayedRevision);

        return observed;
    }

    /// <inheritdoc cref="Observe(Guid, SavegameCheckoutBinding, string, Guid?, int?)"/>
    /// <remarks>Reads the manifest itself, for the caller that has not already.</remarks>
    private SavegameCheckoutBinding Observe(Guid instanceId, SavegameCheckoutBinding binding, string currentContentHash)
    {
        var manifest = manifestStore.TryRead(instanceId);

        return Observe(instanceId, binding, currentContentHash, manifest?.ProfileId, manifest?.ProfileRevision);
    }

    /// <summary>
    /// Refuses to take a second savegame that claims this instance's mod folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The limit is about the folder, not about savegames.</b> One mod folder can only be on one
    /// revision, so two savegames following two mod lists cannot both be played out of one instance - and
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
    private void EnsureModFolderIsFree(LocalInstance instance, Guid savegameId, Guid? profileId, string savegameName)
    {
        if (profileId is null)
        {
            return;
        }

        if (SavegameHoldRules.FindConflictingHold(bindings.GetBindings(instance.Id), savegameId) is not SavegameCheckoutBinding blocking)
        {
            return;
        }

        throw new UserFriendlyException(
            $"'{instance.Name}' is already holding a savegame",
            $"Savegame '{blocking.SavegameId}' is checked out in instance '{instance.Id}' and follows profile '{blocking.ProfileId}', so its mod folder is spoken for. Check that one in before taking '{savegameName}'.");
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
        LocalInstance instance,
        ILocalSavegameAdapter adapter,
        SavegameSlotId slot,
        string savegameName,
        CancellationToken ct)
    {
        var availability = await ClassifyAsync(instance, adapter, slot, ct);

        if (SavegameSlotStates.IsRefused(availability) is false)
        {
            return;
        }

        throw new UserFriendlyException(
            "That slot holds play nobody has checked in",
            $"Writing '{savegameName}' into slot '{slot.Value}' would destroy a savegame that has been played since it was checked out and exists nowhere else. Check that one in first.");
    }

    private async Task<SavegameSlotAvailability> ClassifyAsync(
        LocalInstance instance,
        ILocalSavegameAdapter adapter,
        SavegameSlotId slotId,
        CancellationToken ct)
    {
        var slots = await adapter.GetSlots(ct);
        var slot = slots.FirstOrDefault(x => string.Equals(x.Id.Value, slotId.Value, StringComparison.OrdinalIgnoreCase))
            // A slot the adapter does not list, for a game that can mint them. Nothing is there, so
            // there is nothing to lose - and a game that cannot mint them will refuse the write when
            // it comes to it, which is its call to make and not this one's.
            ?? new SavegameSlot(slotId, null, false, []);

        var binding = bindings.GetBindingForSlot(instance.Id, slot.Id);

        // Hashed only where something claims the slot: without a binding there is no recorded hash to
        // compare against, so the pass would cost a full archive read to change no answer.
        var currentHash = binding is not null && slot.IsOccupied
            ? await packer.HashSlotAsync(adapter, slot.Id, ct)
            : null;

        return SavegameSlotStates.Classify(slot, binding, currentHash);
    }

    /// <summary>
    /// Fetches a version's blob and replaces the slot's contents with it.
    /// </summary>
    /// <remarks>
    /// Staged to a temporary file rather than unpacked from the response stream, because a zip is
    /// read from its central directory at the end and a network stream cannot seek back. Verified
    /// against the hash the server addressed it by on the way past - one pass, no second read - since
    /// what lands here is about to replace somebody's slot.
    /// </remarks>
    private async Task DownloadIntoSlotAsync(
        ILocalSavegameAdapter adapter,
        Guid repoId,
        Guid savegameId,
        string contentHash,
        SavegameSlotId slot,
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
            using (var download = await downloader.OpenAsync(link.Link, ct))
            await using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.Asynchronous))
            {
                await download.Content.CopyToAsync(file, ct);
            }

            await using (var written = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var actual = await ModContentHasher.ComputeAsync(written, ct);

                if (ModContentHasher.Matches(actual, contentHash) is false)
                {
                    throw new UserFriendlyException(
                        "The savegame that arrived is not the one that was asked for",
                        $"Expected content hash '{contentHash}' but the downloaded blob hashes to '{actual}'. Nothing has been written into the slot.");
                }
            }

            await packer.UnpackAsync(archivePath, adapter, slot, ct);
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
    private async Task UploadAsync(Guid repoId, Guid savegameId, PackedSavegame packed, CancellationToken ct)
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

        var uploaded = await uploader.UploadAsync(
            new ModFileUpload(destination, link.ContentHashMetadataKey, () => File.OpenRead(packed.FilePath)),
            ct);

        // The uploader hashes what it actually sent. Disagreeing with the packer means the archive
        // changed under us between the two reads, and committing a version pointing at a blob nobody
        // can reproduce is worse than failing here - where nothing has been recorded yet.
        if (ModContentHasher.Matches(uploaded, packed.ContentHash) is false)
        {
            throw new UserFriendlyException(
                "The savegame changed while it was being uploaded",
                $"Packed as '{packed.ContentHash}' but uploaded '{uploaded}'. No version has been recorded.");
        }
    }

    /// <summary>
    /// The content hash of one numbered version.
    /// </summary>
    /// <remarks>
    /// The head is answered from what the caller already has, which is the overwhelmingly common case
    /// and saves a round trip; anything older costs the version list, which is a page of rows and no
    /// blobs.
    /// </remarks>
    private async Task<string> ResolveVersionHashAsync(SavegameDto savegame, int versionNumber, CancellationToken ct)
    {
        if (savegame.Head is SavegameVersionDto head && head.Number == versionNumber)
        {
            return head.ContentHash;
        }

        var versions = await savegamesClient.GetSavegameVersionsV1Async(savegame.RepoId, savegame.Id, null, _versionPageSize, ct);

        return versions.Versions.FirstOrDefault(x => x.Number == versionNumber)?.ContentHash
            ?? throw new UserFriendlyException(
                $"Version {versionNumber} of '{savegame.Name}' is not there any more",
                $"Savegame '{savegame.Id}' has no version {versionNumber}. Retention keeps the last few versions and anything labelled; pruning leaves the gap where an old one was.");
    }

    /// <summary>
    /// Which revision the version a check-in is about to mint was played on, or null where the
    /// savegame follows no mod list and there is no such thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What was observed beats what is installed.</b> The binding's
    /// <see cref="SavegameCheckoutBinding.LastPlayedRevision"/> is the revision the folder was on the
    /// last time the slot's bytes actually moved, which is the question a version answers; the
    /// manifest only says what the folder runs at this instant, and an apply between the last evening
    /// and the hand-back moves it without anybody playing on it. Preferring the manifest would credit
    /// a fortnight-old session to a mod list it never ran on.
    /// </para>
    /// <para>
    /// The manifest is the fallback rather than the answer, and only where it describes the same
    /// profile the save was checked out against: two revision numbers belonging to two different
    /// profiles are not comparable, and the server rejects a revision that is not the savegame's
    /// profile's, so sending one because the user re-pointed the instance would fail the check-in with
    /// a message about a profile they were not thinking about. What the binding recorded at check-out
    /// is the last resort, and it is still honest - it says which list the save was handed over on.
    /// </para>
    /// </remarks>
    private int? ResolveAppliedRevision(LocalInstance instance, SavegameCheckoutBinding binding)
    {
        // A savegame following no mod list sends no revision at all, and the server refuses one that
        // does. Nothing was attributed to it either: Observe() leaves such a binding alone.
        if (binding.ProfileId is not Guid profileId)
        {
            return null;
        }

        return FindPlayedRevision(instance, binding, profileId)
            ?? throw new UserFriendlyException(
                $"'{instance.Name}' has no record of which mod list it is on",
                $"Neither the sync manifest for instance '{instance.Id}' nor the checkout binding records a profile revision, and a savegame that follows a mod list has to name one. Apply the profile to this instance and check in again.");
    }

    /// <summary>
    /// The three places the number can come from, in order. Split out from
    /// <see cref="ResolveAppliedRevision"/> so the check-in dialog can show the answer without
    /// inheriting its refusal: what it has to say is "played on rev 1004", and it has nothing useful
    /// to say about a savegame whose revision nothing on this machine knows.
    /// </summary>
    private int? FindPlayedRevision(LocalInstance instance, SavegameCheckoutBinding binding, Guid profileId)
    {
        if (binding.LastPlayedRevision is int played)
        {
            return played;
        }

        var manifest = manifestStore.TryRead(instance.Id);

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
    private void Recycle(ILocalSavegameAdapter adapter, SavegameSlotId slot)
    {
        try
        {
            var path = adapter.GetSlotPath(slot);

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
    /// so what is recorded describes the version being minted rather than whatever that slot holds
    /// later.
    /// </para>
    /// <para>
    /// <b>Never allowed to fail the write.</b> A map name is decoration; the save is the thing. An
    /// adapter that throws, or a slot the game is holding open, costs the details and nothing else -
    /// same treatment mod imagery gets, and for the same reason.
    /// </para>
    /// </remarks>
    private async Task<List<SavegameDetailDto>> DescribeAsync(
        ILocalSavegameAdapter adapter, SavegameSlotId slot, CancellationToken ct)
    {
        try
        {
            var slots = await adapter.GetSlots(ct);

            return [.. slots
                .FirstOrDefault(x => x.Id == slot)?.Details
                    .Select(x => new SavegameDetailDto { Key = x.Id, Label = x.Label, Value = x.Value })
                ?? []];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not describe slot {Slot}; the version will carry no details.", slot.Value);

            return [];
        }
    }

    private ILocalSavegameAdapter RequireAdapter(LocalInstance instance)
        => adapters.TryGet(instance)
            ?? throw new UserFriendlyException(
                $"'{instance.Name}' has no savegames",
                $"No loaded repo hydrates a savegame adapter for instance '{instance.Id}' - either its game does not support savegames, or no repo on this machine serves its scope.");

    /// <summary>Slots, or nothing where the game folder is unreachable - unknown, never drifted.</summary>
    private async Task<IReadOnlyList<SavegameSlot>> ReadSlotsOrNothing(ILocalSavegameAdapter adapter, CancellationToken ct)
    {
        try
        {
            return await adapter.GetSlots(ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreachable game folder reads as "no slots", which is deliberately indistinguishable
            // from an empty one to everything above - so this is the only place it is visible.
            logger.LogWarning(exception, "Could not read the savegame slots; treating the instance as having none.");

            return [];
        }
    }

    /// <summary>
    /// One slot's hash, or null where reading it failed. Null reports no drift rather than reporting
    /// play, for the reason <see cref="SavegameDriftRules"/> gives: a warning that fires when nothing
    /// is wrong is one everybody learns to click past.
    /// </summary>
    private async Task<string?> HashOrNothing(ILocalSavegameAdapter adapter, SavegameSlotId slot, CancellationToken ct)
    {
        try
        {
            return await packer.HashSlotAsync(adapter, slot, ct);
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
