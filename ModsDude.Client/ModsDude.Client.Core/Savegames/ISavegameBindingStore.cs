using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Savegames;

public interface ISavegameBindingStore
{
    /// <summary>
    /// Raised after a binding is taken, released or forgotten.
    /// </summary>
    /// <remarks>
    /// What this machine is holding is half of what the drift notice reports, and it changes without
    /// anything touching a mod folder or a profile - so a check-out taken and a save checked back in
    /// are both moments the notice's answer is stale and nothing else would say so. See
    /// docs/07-mod-sync-design.md#it-has-to-be-unmissable-everywhere.
    /// </remarks>
    event EventHandler? BindingsChanged;

    /// <summary>
    /// The revision this game is held on by its own intent rather than by a savegame, where it follows
    /// <paramref name="profileId"/> - see <see cref="PersistedGame.PinnedRevision"/>.
    /// </summary>
    /// <remarks>
    /// Read here, beside the bindings, because it answers the same question they do - which revision
    /// the folder has to be on - and is kept in the same record under the same save.
    /// </remarks>
    int? GetPinnedRevision(GameIdentity game, Guid profileId);

    /// <summary>
    /// What this game holds for one savegame, or null where it holds none.
    /// </summary>
    /// <remarks>
    /// Keyed on the savegame id alone rather than on <c>(RepoId, SavegameId)</c>: the id is a Guid
    /// minted by the server and unique across repos, and every caller here already has one savegame
    /// in hand rather than a repo to search. The repo id travels on the binding for the callers that
    /// need to talk to a server about it.
    /// </remarks>
    SavegameCheckoutBinding? GetBinding(GameIdentity game, Guid savegameId);

    /// <summary>
    /// What this game holds in one slot, or null where the slot holds nothing ModsDude checked
    /// out. Null does <b>not</b> mean the slot is empty - see
    /// <see cref="SavegameSlotAvailability.Unrecognised"/>, which is exactly this answer combined
    /// with an occupied slot.
    /// </summary>
    /// <remarks>
    /// The whole reference, never the slot id alone: two of a game's targets numbering their slots
    /// from one is the ordinary case, and asking with a bare id would hand back the other folder's
    /// hold - a binding declaring a slot protected that nothing ever wrote to.
    /// </remarks>
    SavegameCheckoutBinding? GetBindingForSlot(GameIdentity game, SavegameSlotRef slot);

    /// <summary>
    /// Everything this game holds in one of its targets - the folder half of a hold, for the callers
    /// that are about one folder rather than about the game.
    /// </summary>
    /// <remarks>
    /// Play attribution is exactly like this: a save in target T's savegame folder was played against
    /// target T's mod folder, so an apply to one folder has nothing to say about what is held in
    /// another.
    /// </remarks>
    IReadOnlyList<SavegameCheckoutBinding> GetBindingsIn(ModTargetRef target);

    /// <summary>
    /// Everything this game currently holds. A short list by construction - a slot is occupied by
    /// ModsDude only while a save is checked out, which is one or two, not twenty.
    /// </summary>
    IReadOnlyList<SavegameCheckoutBinding> GetBindings(GameIdentity game);

    /// <summary>
    /// Records that a savegame is now checked out into a slot, and remembers the slot as this
    /// savegame's hint for next time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>At most one binding per savegame and one per slot.</b> Setting one that collides on either
    /// replaces it, so the two invariants hold by construction rather than by every caller
    /// remembering to clear first. A savegame moving to a new slot leaves nothing behind claiming the
    /// old one; a slot receiving a different savegame stops claiming the previous one. Both are
    /// states the safety check in <see cref="SavegameSlotStates"/> would otherwise read as unpublished
    /// play forever.
    /// </para>
    /// <para>
    /// The hint is written here rather than at check-in because this is the moment the fact becomes
    /// true. Writing it at check-in instead would lose it for any savegame still checked out when the
    /// app is closed.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No such game is configured on this machine.</exception>
    void SetBinding(GameIdentity game, SavegameCheckoutBinding binding);

    /// <summary>
    /// Releases the slot a savegame was holding - a check-in, a discard, or a take-over.
    /// </summary>
    /// <remarks>
    /// <b>The hint is kept.</b> That asymmetry is the design and not an oversight: the binding exists
    /// only while the save is checked out and is a lie the moment it is not, while the hint's entire
    /// job is the <em>next</em> check-out. Clearing both would make every second check-out of the same
    /// save a blank picker, which is the memory test this design exists to remove.
    /// </remarks>
    void ClearBinding(GameIdentity game, Guid savegameId);

    /// <summary>
    /// Forgets a savegame entirely on this machine - the binding <em>and</em> the slot hint - without
    /// touching the slot's contents or telling the server anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one operation that clears the hint</b>, and the reason it is separate from
    /// <see cref="ClearBinding"/> rather than a flag on it. A check-in releases the slot and keeps the
    /// hint on purpose, because there is going to be a next check-out of the same save; this is the
    /// opposite intent - there is not, and a hint pointing at a slot on behalf of a savegame the user
    /// has disowned is a suggestion nobody asked for.
    /// </para>
    /// <para>
    /// <b>Local only, and deliberately.</b> The two cases are a savegame the repo no longer has - so
    /// there is no claim left to release and the server would answer 404 - and a save the user wants
    /// to keep playing as their own. Neither is served by a server call, and one of them cannot have
    /// one. Whether the claim also has to be handed back is the caller's question, and
    /// <c>DiscardAsync</c> is the answer where it does.
    /// </para>
    /// </remarks>
    /// <returns>False where this game knew nothing about the savegame, which is idempotent rather than an error.</returns>
    bool Forget(GameIdentity game, Guid savegameId);

    /// <summary>
    /// The slot this savegame was last written into on this machine, or null where it never has been.
    /// </summary>
    /// <remarks>
    /// <b>Returned exactly as recorded, however wrong it is.</b> The slot may since have been filled
    /// by something else, or stopped existing - this deliberately does not look. Validating here would
    /// put the answer "the remembered slot is taken, here is the first free one" in two places, and
    /// the picker is the one that can say it.
    /// </remarks>
    SavegameSlotRef? GetSlotHint(GameIdentity game, Guid savegameId);
}
