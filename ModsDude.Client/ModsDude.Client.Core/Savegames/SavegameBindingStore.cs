using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// The persisted instance record this store reads and writes, and the one call that flushes it to
/// disk.
/// </summary>
/// <remarks>
/// <para>
/// A seam of two members over <see cref="StateStore"/>, for one reason: <see cref="Store{T}"/> writes
/// to a fixed path under LocalAppData, so a test exercising the store as-is would rewrite the
/// developer's own <c>state.json</c>. The rules being tested here - one binding per savegame, one per
/// slot, and a hint that outlives its binding - are pure bookkeeping over two lists, and none of them
/// is about json.
/// </para>
/// <para>
/// Deliberately not a repository of its own: the bindings live on
/// <see cref="PersistedLocalInstance"/>, in the same file and under the same save as the instance's
/// active profile, because losing one without the other would leave a slot held by a savegame nothing
/// can name.
/// </para>
/// </remarks>
public interface IPersistedInstanceState
{
    /// <summary>The instance's persisted record, or null where no such instance is configured.</summary>
    PersistedLocalInstance? Find(Guid instanceId);

    /// <summary>Flushes every pending change. Whole-state, exactly as the rest of the client saves.</summary>
    void Save();
}


/// <summary><see cref="IPersistedInstanceState"/> over the real <c>state.json</c>.</summary>
public sealed class StateStoreInstanceState(StateStore store) : IPersistedInstanceState
{
    public PersistedLocalInstance? Find(Guid instanceId)
        => store.Get().Instances.TryGetValue(instanceId, out var instance) ? instance : null;

    // Store.Save() serialises the entire LocalState, so there is nothing finer to flush and no
    // ordering to get wrong - the same call LocalInstanceRepository makes after every mutation.
    public void Save() => store.Save();
}


/// <summary>
/// Which savegame is checked out into which slot on this machine, and where each savegame was last
/// put.
/// </summary>
/// <remarks>
/// <para>
/// <b>The binding is a source of truth, not a cache.</b> Once somebody has played, the bytes in the
/// slot match no version on the server, so nothing afterwards can work out which savegame that slot
/// was - losing this loses the ability to check the save back in at all. Same argument as
/// <see cref="ActiveProfile"/>, and the same conclusion: persisted, and written before anything else
/// depends on it.
/// </para>
/// <para>
/// <b>The hint is the opposite kind of thing.</b> It is advisory, it survives the check-in that
/// destroys the binding, and it is worth nothing when wrong. It is never repaired and never validated
/// on read: the slot it names may since have been filled by something else, and finding that out is
/// the picker's job at the moment it pre-selects - not this store's on the way past.
/// </para>
/// <para>
/// Every mutation saves immediately rather than batching. The window this closes is the one where the
/// app writes a savegame into a slot and is killed before recording that it did, which leaves an
/// unrecognised folder holding play that ModsDude put there and can no longer name.
/// </para>
/// </remarks>
public sealed class SavegameBindingStore(IPersistedInstanceState state)
{
    /// <summary>
    /// What this instance holds for one savegame, or null where it holds none.
    /// </summary>
    /// <remarks>
    /// Keyed on the savegame id alone rather than on <c>(RepoId, SavegameId)</c>: the id is a Guid
    /// minted by the server and unique across repos, and every caller here already has one savegame
    /// in hand rather than a repo to search. The repo id travels on the binding for the callers that
    /// need to talk to a server about it.
    /// </remarks>
    public SavegameCheckoutBinding? GetBinding(Guid instanceId, Guid savegameId)
    {
        return FirstOrNull(Bindings(instanceId), x => x.SavegameId == savegameId);
    }

    /// <summary>
    /// What this instance holds in one slot, or null where the slot holds nothing ModsDude checked
    /// out. Null does <b>not</b> mean the slot is empty - see
    /// <see cref="SavegameSlotAvailability.Unrecognised"/>, which is exactly this answer combined
    /// with an occupied slot.
    /// </summary>
    public SavegameCheckoutBinding? GetBindingForSlot(Guid instanceId, string slotId)
    {
        return FirstOrNull(Bindings(instanceId), x => SlotIdsMatch(x.SlotId, slotId));
    }

    /// <inheritdoc cref="GetBindingForSlot(Guid, string)"/>
    public SavegameCheckoutBinding? GetBindingForSlot(Guid instanceId, SavegameSlotId slotId)
        => GetBindingForSlot(instanceId, slotId.Value);

    /// <summary>
    /// Everything this instance currently holds. A short list by construction - a slot is occupied by
    /// ModsDude only while a save is checked out, which is one or two, not twenty.
    /// </summary>
    public IReadOnlyList<SavegameCheckoutBinding> GetBindings(Guid instanceId)
    {
        return Bindings(instanceId) is List<SavegameCheckoutBinding> bindings ? [.. bindings] : [];
    }

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
    /// <exception cref="InvalidOperationException">No such instance is configured on this machine.</exception>
    public void SetBinding(Guid instanceId, SavegameCheckoutBinding binding)
    {
        // Refused rather than ignored. Silently dropping this loses the only record of which
        // savegame is sitting in that slot, and the folder is already written by the time anybody
        // would notice.
        var instance = state.Find(instanceId)
            ?? throw new InvalidOperationException($"No local instance '{instanceId}' to bind a savegame to.");

        instance.SavegameCheckouts.RemoveAll(x =>
            x.SavegameId == binding.SavegameId ||
            SlotIdsMatch(x.SlotId, binding.SlotId));

        instance.SavegameCheckouts.Add(binding);

        SetHint(instance, new SavegameSlotHint(binding.RepoId, binding.SavegameId, binding.SlotId));

        state.Save();
    }

    /// <summary>
    /// Releases the slot a savegame was holding - a check-in, a discard, or a take-over.
    /// </summary>
    /// <remarks>
    /// <b>The hint is kept.</b> That asymmetry is the design and not an oversight: the binding exists
    /// only while the save is checked out and is a lie the moment it is not, while the hint's entire
    /// job is the <em>next</em> check-out. Clearing both would make every second check-out of the same
    /// save a blank picker, which is the memory test this design exists to remove.
    /// </remarks>
    public void ClearBinding(Guid instanceId, Guid savegameId)
    {
        // A binding that is already gone is the state the caller wanted, so this is idempotent - a
        // check-in retried after a crash must not fail on its own success.
        if (state.Find(instanceId) is not PersistedLocalInstance instance)
        {
            return;
        }

        if (instance.SavegameCheckouts.RemoveAll(x => x.SavegameId == savegameId) == 0)
        {
            return;
        }

        state.Save();
    }

    /// <summary>
    /// The slot this savegame was last written into on this machine, or null where it never has been.
    /// </summary>
    /// <remarks>
    /// <b>Returned exactly as recorded, however wrong it is.</b> The slot may since have been filled
    /// by something else, or stopped existing - this deliberately does not look. Validating here would
    /// put the answer "the remembered slot is taken, here is the first free one" in two places, and
    /// the picker is the one that can say it.
    /// </remarks>
    public string? GetSlotHint(Guid instanceId, Guid savegameId)
    {
        return state.Find(instanceId)
            ?.SavegameSlotHints
            .Where(x => x.SavegameId == savegameId)
            .Select(x => x.SlotId)
            .FirstOrDefault();
    }


    private List<SavegameCheckoutBinding>? Bindings(Guid instanceId) => state.Find(instanceId)?.SavegameCheckouts;

    /// <summary>
    /// The first matching binding, or null for none.
    /// </summary>
    /// <remarks>
    /// Written out rather than <c>FirstOrDefault</c> because <see cref="SavegameCheckoutBinding"/> is
    /// a struct: the default it hands back is a fully-formed binding with an empty slot id, a zero
    /// version and an empty hash, and every caller here reads that as a real one. A slot safety check
    /// handed that binding declares the slot held with unpublished play and refuses to write to it.
    /// </remarks>
    private static SavegameCheckoutBinding? FirstOrNull(
        IEnumerable<SavegameCheckoutBinding>? bindings,
        Func<SavegameCheckoutBinding, bool> predicate)
    {
        if (bindings is null)
        {
            return null;
        }

        foreach (var binding in bindings)
        {
            if (predicate(binding))
            {
                return binding;
            }
        }

        return null;
    }

    /// <summary>
    /// One hint per savegame. Unlike a binding, a hint has no per-slot uniqueness to keep: two
    /// savegames may perfectly well remember the same slot, having taken turns in it.
    /// </summary>
    private static void SetHint(PersistedLocalInstance instance, SavegameSlotHint hint)
    {
        instance.SavegameSlotHints.RemoveAll(x => x.SavegameId == hint.SavegameId);
        instance.SavegameSlotHints.Add(hint);
    }

    /// <summary>
    /// Whether two adapter slot ids address the same place. Case-insensitive for the same reason the
    /// safety check is: these are folder and save names on Windows, where two spellings are one
    /// place, and treating them as two would let a second savegame claim a slot already held.
    /// </summary>
    private static bool SlotIdsMatch(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
