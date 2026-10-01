using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// The persisted game record this store reads and writes, and the one call that flushes it to
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
/// <see cref="PersistedGame"/>, in the same file and under the same save as the game's
/// active profile, because losing one without the other would leave a slot held by a savegame nothing
/// can name.
/// </para>
/// </remarks>
public interface IPersistedGameState
{
    /// <summary>
    /// Reads the game's persisted record - null where no such game is configured - under the state's
    /// lock. Whatever is returned must be a copy, never one of its lists.
    /// </summary>
    T Read<T>(GameIdentity game, Func<PersistedGame?, T> read);

    /// <summary>Changes the game's record under the state's lock, and saves where the change says it changed anything.</summary>
    bool Update(GameIdentity game, Func<PersistedGame?, bool> update);
}


/// <summary><see cref="IPersistedGameState"/> over the real <c>state.json</c>.</summary>
public sealed class StateStoreGameState(IStateStore store) : IPersistedGameState
{
    public T Read<T>(GameIdentity game, Func<PersistedGame?, T> read)
        => store.Read(state => read(state.Games.GetValueOrDefault(game)));

    public bool Update(GameIdentity game, Func<PersistedGame?, bool> update)
        => store.UpdateIf(state => update(state.Games.GetValueOrDefault(game)));
}


/// <summary>
/// Which savegame is checked out into which slot on this machine, and where each savegame was last
/// put.
/// </summary>
/// <remarks>
/// <para>
/// <b>The binding is a source of truth, not a cache.</b> Once somebody has played, the bytes in the
/// slot match no snapshot on the server, so nothing afterwards can work out which savegame that slot
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
/// <para>
/// <b>A hold outlives the target it names, deliberately.</b> Emptying a settings field takes a target
/// away, and an adapter author renaming a key does exactly the same thing from here - so nothing
/// sweeps bindings the way stale manifests are swept. A manifest nobody reads costs a rescan; a
/// binding is a savegame this machine is still holding and a claim somebody else is waiting on, and
/// dropping one would leave a slot full of somebody's evening that nothing can name. The game says
/// so where holds are shown, and filling the field back in makes the hold addressable again.
/// </para>
/// </remarks>
public sealed class SavegameBindingStore(IPersistedGameState state) : ISavegameBindingStore
{
    public event EventHandler? BindingsChanged;


    public int? GetPinnedRevision(GameIdentity game, Guid profileId)
    {
        return state.Read(game, persisted => persisted is not null && persisted.ActiveProfile?.ProfileId == profileId ? persisted.PinnedRevision : null);
    }


    public SavegameCheckoutBinding? GetBinding(GameIdentity game, Guid savegameId)
    {
        return FirstOrNull(Bindings(game), x => x.SavegameId == savegameId);
    }

    public SavegameCheckoutBinding? GetBindingForSlot(GameIdentity game, SavegameSlotRef slot)
    {
        return FirstOrNull(Bindings(game), x => x.Slot.Addresses(slot));
    }

    public IReadOnlyList<SavegameCheckoutBinding> GetBindingsIn(ModTargetRef target)
    {
        return [.. GetBindings(target.Game).Where(x => x.Slot.Target == target.Key)];
    }

    public IReadOnlyList<SavegameCheckoutBinding> GetBindings(GameIdentity game)
    {
        return Bindings(game);
    }

    public void SetBinding(GameIdentity game, SavegameCheckoutBinding binding)
    {
        // Refused rather than ignored. Silently dropping this loses the only record of which
        // savegame is sitting in that slot, and the folder is already written by the time anybody
        // would notice.
        state.Update(game, persisted =>
        {
            if (persisted is null)
            {
                throw new InvalidOperationException($"No local game '{game}' to bind a savegame to.");
            }

            persisted.SavegameCheckouts.RemoveAll(x =>
                x.SavegameId == binding.SavegameId ||
                x.Slot.Addresses(binding.Slot));

            persisted.SavegameCheckouts.Add(binding);

            SetHint(persisted, new SavegameSlotHint(binding.RepoId, binding.SavegameId, binding.Slot));

            return true;
        });

        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearBinding(GameIdentity game, Guid savegameId)
    {
        // A binding that is already gone is the state the caller wanted, so this is idempotent - a
        // check-in retried after a crash must not fail on its own success.
        var removed = state.Update(game, persisted =>
            persisted is not null && persisted.SavegameCheckouts.RemoveAll(x => x.SavegameId == savegameId) > 0);

        if (removed)
        {
            BindingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool Forget(GameIdentity game, Guid savegameId)
    {
        var removed = state.Update(game, persisted =>
            persisted is not null &&
            persisted.SavegameCheckouts.RemoveAll(x => x.SavegameId == savegameId)
                + persisted.SavegameSlotHints.RemoveAll(x => x.SavegameId == savegameId) > 0);

        if (removed)
        {
            BindingsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public SavegameSlotRef? GetSlotHint(GameIdentity game, Guid savegameId)
    {
        // Not FirstOrDefault, for the reason FirstOrNull exists: SavegameSlotHint is a struct, and
        // the default one carries a slot reference nobody can address.
        return state.Read(game, persisted => persisted?.SavegameSlotHints
            .Where(x => x.SavegameId == savegameId)
            .Select(x => (SavegameSlotRef?)x.Slot)
            .FirstOrDefault());
    }


    private IReadOnlyList<SavegameCheckoutBinding> Bindings(GameIdentity game)
        => state.Read<IReadOnlyList<SavegameCheckoutBinding>>(game, persisted => persisted is null ? [] : [.. persisted.SavegameCheckouts]);

    /// <summary>
    /// The first matching binding, or null for none.
    /// </summary>
    /// <remarks>
    /// Written out rather than <c>FirstOrDefault</c> because <see cref="SavegameCheckoutBinding"/> is
    /// a struct: the default it hands back is a fully-formed binding with a blank slot reference, a zero
    /// snapshot and an empty hash, and every caller here reads that as a real one. A slot safety check
    /// handed that binding declares the slot held with unpublished play and refuses to write to it.
    /// </remarks>
    private static SavegameCheckoutBinding? FirstOrNull(
        IEnumerable<SavegameCheckoutBinding> bindings,
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
    private static void SetHint(PersistedGame persisted, SavegameSlotHint hint)
    {
        persisted.SavegameSlotHints.RemoveAll(x => x.SavegameId == hint.SavegameId);
        persisted.SavegameSlotHints.Add(hint);
    }
}
