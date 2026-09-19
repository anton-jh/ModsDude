using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameBindingStoreTests
{
    private readonly GameIdentity _game = new("farmingSimulator", "fs25");
    private readonly Guid _repoId = Guid.NewGuid();


    [Fact]
    public void A_binding_survives_a_round_trip_intact()
    {
        var (store, _) = Store();
        var savegameId = Guid.NewGuid();
        var binding = Binding(savegameId, "savegame3", snapshot: 7, hash: "aaaa");

        store.SetBinding(_game, binding);

        var read = store.GetBinding(_game, savegameId);

        Assert.NotNull(read);
        Assert.Equal(binding, read);

        // The snapshot and the hash are the two halves a check-in needs: the base it was built on, and
        // whether the slot has moved since. Neither can be re-derived once somebody has played.
        Assert.Equal(7, read.Value.Snapshot);
        Assert.Equal("aaaa", read.Value.ContentHash);
    }

    [Fact]
    public void A_slot_can_be_looked_up_by_its_id()
    {
        var (store, _) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));

        Assert.Equal(savegameId, store.GetBindingForSlot(_game, Keys.Slot("savegame3"))?.SavegameId);
        Assert.Null(store.GetBindingForSlot(_game, Keys.Slot("savegame4")));

        // The same case-insensitivity the safety check uses. A lookup that missed here would report
        // an occupied slot as unrecognised and recycle a save ModsDude itself put there.
        Assert.Equal(savegameId, store.GetBindingForSlot(_game, Keys.Slot("SAVEGAME3"))?.SavegameId);
    }

    /// <summary>
    /// A savegame is checked out to at most one slot per game. Moving it must leave nothing
    /// behind claiming the old slot, or that slot reads as held forever and can never be written to
    /// again.
    /// </summary>
    [Fact]
    public void Moving_a_savegame_to_another_slot_replaces_its_binding()
    {
        var (store, state) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));
        store.SetBinding(_game, Binding(savegameId, "savegame7"));

        Assert.Single(Held(state).SavegameCheckouts);
        Assert.Equal(Keys.Slot("savegame7"), store.GetBinding(_game, savegameId)?.Slot);
        Assert.Null(store.GetBindingForSlot(_game, Keys.Slot("savegame3")));
    }

    /// <summary>
    /// And one slot holds at most one savegame. The invariant holds by construction rather than by
    /// every caller remembering to clear first - which is what lets a check-in act without asking
    /// anything.
    /// </summary>
    [Fact]
    public void A_second_savegame_written_into_a_slot_replaces_the_first_binding()
    {
        var (store, state) = Store();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        store.SetBinding(_game, Binding(first, "savegame3"));
        store.SetBinding(_game, Binding(second, "savegame3"));

        Assert.Single(Held(state).SavegameCheckouts);
        Assert.Equal(second, store.GetBindingForSlot(_game, Keys.Slot("savegame3"))?.SavegameId);
        Assert.Null(store.GetBinding(_game, first));
    }

    [Fact]
    public void A_slot_id_differing_only_in_casing_collides_with_the_binding_already_there()
    {
        var (store, state) = Store();

        store.SetBinding(_game, Binding(Guid.NewGuid(), "savegame3"));
        store.SetBinding(_game, Binding(Guid.NewGuid(), "Savegame3"));

        Assert.Single(Held(state).SavegameCheckouts);
    }

    /// <summary>
    /// <b>Two folders numbering their slots from one is the ordinary case</b>, and the reason a slot
    /// is addressed by a reference rather than by an id. Keyed on the id alone, the server's savegame
    /// and the client's would be one binding, and the one-per-slot rule would enforce the collision
    /// rather than catch it.
    /// </summary>
    [Fact]
    public void The_same_slot_id_in_two_targets_is_two_slots()
    {
        var (store, _) = Store();
        var onTheServer = Guid.NewGuid();
        var onTheClient = Guid.NewGuid();

        store.SetBinding(_game, Binding(onTheServer, "savegame1", target: "server"));
        store.SetBinding(_game, Binding(onTheClient, "savegame1", target: "client"));

        Assert.Equal(2, store.GetBindings(_game).Count);
        Assert.Equal(onTheServer, store.GetBindingForSlot(_game, Keys.Slot("savegame1", "server"))?.SavegameId);
        Assert.Equal(onTheClient, store.GetBindingForSlot(_game, Keys.Slot("savegame1", "client"))?.SavegameId);
    }

    /// <summary>
    /// The holds in one folder, which is what play attribution is about: an apply moves one folder's
    /// mod list, and only the saves sitting in that folder were played against it.
    /// </summary>
    [Fact]
    public void The_holds_in_one_target_are_that_targets_own()
    {
        var (store, _) = Store();
        var onTheServer = Guid.NewGuid();

        store.SetBinding(_game, Binding(onTheServer, "savegame1", target: "server"));
        store.SetBinding(_game, Binding(Guid.NewGuid(), "savegame1", target: "client"));

        var held = store.GetBindingsIn(new ModTargetRef(_game, new TargetKey("server")));

        Assert.Equal(onTheServer, Assert.Single(held).SavegameId);
    }

    /// <summary>
    /// <b>A hold outlives the target it names.</b> Emptying a settings field, and an adapter author
    /// renaming a key, both take a folder away without asking - and the savegame sitting in it is
    /// still on this disk and still claimed on the server. A stale manifest is dropped for exactly
    /// this event; a binding is not, because losing one loses the only record of which savegame is in
    /// that folder.
    /// </summary>
    [Fact]
    public void A_hold_survives_the_target_it_was_taken_in()
    {
        var (store, _) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame1", target: "client"));

        // The settings edit happens entirely outside this store - there is no call it makes and no
        // event it hears. That is the point: nothing here has to notice, and nothing here drops it.
        var held = Assert.Single(store.GetBindings(_game));

        Assert.Equal(savegameId, held.SavegameId);
        Assert.Equal(Keys.Slot("savegame1", "client"), held.Slot);
    }

    [Fact]
    public void Several_savegames_in_several_slots_are_all_held_at_once()
    {
        var (store, _) = Store();

        store.SetBinding(_game, Binding(Guid.NewGuid(), "savegame1"));
        store.SetBinding(_game, Binding(Guid.NewGuid(), "savegame2"));

        Assert.Equal(2, store.GetBindings(_game).Count);
    }

    /// <summary>
    /// <b>The asymmetry that is the whole design.</b> The binding exists only while the save is
    /// checked out and is a lie the moment it is not; the hint's entire job is the <em>next</em>
    /// check-out. Clearing both would make every second check-out of the same save a blank picker.
    /// </summary>
    [Fact]
    public void Clearing_a_binding_keeps_the_slot_hint()
    {
        var (store, _) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));
        store.ClearBinding(_game, savegameId);

        Assert.Null(store.GetBinding(_game, savegameId));
        Assert.Equal(Keys.Slot("savegame3"), store.GetSlotHint(_game, savegameId));
    }

    [Fact]
    public void Clearing_one_binding_leaves_the_others_alone()
    {
        var (store, _) = Store();
        var kept = Guid.NewGuid();
        var cleared = Guid.NewGuid();

        store.SetBinding(_game, Binding(kept, "savegame1"));
        store.SetBinding(_game, Binding(cleared, "savegame2"));

        store.ClearBinding(_game, cleared);

        Assert.Single(store.GetBindings(_game));
        Assert.NotNull(store.GetBinding(_game, kept));
    }

    /// <summary>
    /// A check-in retried after a crash must not fail on its own success, so clearing a binding that
    /// is already gone is the state the caller wanted rather than an error.
    /// </summary>
    [Fact]
    public void Clearing_a_binding_that_is_not_there_is_not_an_error()
    {
        var (store, state) = Store();

        store.ClearBinding(_game, Guid.NewGuid());
        store.ClearBinding(Keys.Game("beamng"), Guid.NewGuid());

        Assert.Empty(store.GetBindings(_game));

        // Nothing changed, so nothing was written. state.json is rewritten whole, and rewriting it
        // for a no-op is a file operation on every idle check.
        Assert.Equal(0, state.Saves);
    }

    /// <summary>
    /// The hint is written when the savegame is checked out, not when it is checked in. Writing it at
    /// check-in would lose it for any savegame still held when the app is closed - which is every
    /// savegame somebody is actually playing.
    /// </summary>
    [Fact]
    public void Checking_out_records_the_hint_immediately()
    {
        var (store, _) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));

        Assert.Equal(Keys.Slot("savegame3"), store.GetSlotHint(_game, savegameId));
    }

    [Fact]
    public void A_savegame_that_moves_slots_remembers_only_the_latest()
    {
        var (store, state) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));
        store.SetBinding(_game, Binding(savegameId, "savegame7"));

        Assert.Single(Held(state).SavegameSlotHints);
        Assert.Equal(Keys.Slot("savegame7"), store.GetSlotHint(_game, savegameId));
    }

    /// <summary>
    /// Two savegames may perfectly well remember the same slot, having taken turns in it. The hint
    /// has no per-slot uniqueness to keep, because it claims nothing.
    /// </summary>
    [Fact]
    public void Two_savegames_may_remember_the_same_slot()
    {
        var (store, _) = Store();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        store.SetBinding(_game, Binding(first, "savegame3"));
        store.ClearBinding(_game, first);
        store.SetBinding(_game, Binding(second, "savegame3"));

        Assert.Equal(Keys.Slot("savegame3"), store.GetSlotHint(_game, first));
        Assert.Equal(Keys.Slot("savegame3"), store.GetSlotHint(_game, second));
    }

    /// <summary>
    /// <b>Never repaired, never validated on read.</b> The remembered slot being taken by something
    /// else is the picker's sentence to say - "the remembered one is taken, here is the first free
    /// slot" - and it can only say it if the hint comes back exactly as recorded.
    /// </summary>
    [Fact]
    public void A_hint_naming_a_slot_something_else_now_holds_is_returned_unchanged()
    {
        var (store, _) = Store();
        var moved = Guid.NewGuid();

        store.SetBinding(_game, Binding(moved, "savegame3"));
        store.ClearBinding(_game, moved);

        // Somebody else's savegame is now in slot 3.
        store.SetBinding(_game, Binding(Guid.NewGuid(), "savegame3"));

        Assert.Equal(Keys.Slot("savegame3"), store.GetSlotHint(_game, moved));
    }

    [Fact]
    public void A_savegame_never_checked_out_here_has_no_hint()
    {
        var (store, _) = Store();

        Assert.Null(store.GetSlotHint(_game, Guid.NewGuid()));
    }

    /// <summary>
    /// An unknown game is a question about a machine this one is not, so it reads as "holds
    /// nothing" rather than throwing. Every read here is on a path that also runs for games whose
    /// scope no repo on this machine serves.
    /// </summary>
    [Fact]
    public void An_unknown_game_holds_nothing()
    {
        var (store, _) = Store();
        var unknown = Keys.Game("beamng");

        Assert.Empty(store.GetBindings(unknown));
        Assert.Null(store.GetBinding(unknown, Guid.NewGuid()));
        Assert.Null(store.GetBindingForSlot(unknown, Keys.Slot("savegame1")));
        Assert.Null(store.GetSlotHint(unknown, Guid.NewGuid()));
    }

    /// <summary>
    /// Writing is the exception. Silently dropping a binding loses the only record of which savegame
    /// is sitting in that slot, and the folder is already written by the time anybody would notice.
    /// </summary>
    [Fact]
    public void Binding_a_savegame_to_an_unknown_game_is_refused()
    {
        var (store, _) = Store();

        Assert.Throws<InvalidOperationException>(
            () => store.SetBinding(Keys.Game("beamng"), Binding(Guid.NewGuid(), "savegame1")));
    }

    /// <summary>
    /// Every mutation flushes immediately. The window this closes is the one where the app writes a
    /// savegame into a slot and is killed before recording that it did, leaving an unrecognised
    /// folder holding play ModsDude put there and can no longer name.
    /// </summary>
    [Fact]
    public void Every_change_is_persisted_as_it_is_made()
    {
        var (store, state) = Store();
        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));
        Assert.Equal(1, state.Saves);

        store.ClearBinding(_game, savegameId);
        Assert.Equal(2, state.Saves);
    }

    [Fact]
    public void Bindings_are_kept_per_game()
    {
        var other = Keys.Game("beamng");
        var (store, state) = Store();
        state.Add(other, Game());

        var savegameId = Guid.NewGuid();

        store.SetBinding(_game, Binding(savegameId, "savegame3"));

        Assert.Empty(store.GetBindings(other));
        Assert.Null(store.GetSlotHint(other, savegameId));
    }


    private (SavegameBindingStore Store, FakeGameState State) Store()
    {
        var state = new FakeGameState();
        state.Add(_game, Game());

        return (new SavegameBindingStore(state), state);
    }

    /// <summary>
    /// The persisted record itself, for the assertions about what is <em>not</em> in the two lists.
    /// "One binding replaced another" and "one binding was added beside another" both read as one
    /// binding through the public surface, and only the list length tells them apart.
    /// </summary>
    private PersistedGame Held(FakeGameState state) => state.Find(_game)!;

    /// <param name="target">
    /// Which of the game's folders the slot is in. Defaulted, because nearly every game has one and
    /// a test about the rules should not have to say so; the two-folder tests name it.
    /// </param>
    private SavegameCheckoutBinding Binding(
        Guid savegameId, string slotId, int snapshot = 1, string hash = "aaaa", string target = "mods") => new(
        _repoId,
        savegameId,
        Keys.Slot(slotId, target),
        snapshot,
        hash,
        DateTime.UtcNow);

    private static PersistedGame Game() => new()
    {
        GameAdapterId = new GameAdapterId("farmingSimulator", 1),
        Name = "Farming Simulator 25",
        AdapterLocalSettings = "{}"
    };


    /// <summary>
    /// The persisted games, in memory. <c>state.json</c> lives at a fixed path under LocalAppData,
    /// so a test running against the real store would rewrite the developer's own game list - and
    /// none of the rules under test is about json.
    /// </summary>
    private sealed class FakeGameState : IPersistedGameState
    {
        private readonly Dictionary<GameIdentity, PersistedGame> _games = [];


        /// <summary>Counted, because "saves on every change" is itself one of the rules.</summary>
        public int Saves { get; private set; }


        public void Add(GameIdentity identity, PersistedGame game) => _games[identity] = game;

        public PersistedGame? Find(GameIdentity identity)
            => _games.TryGetValue(identity, out var game) ? game : null;

        public void Save() => Saves++;
    }
}
