using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegamePendingPublishesTests
{
    private readonly GameIdentity _game = new("farmingSimulator", "fs25");


    [Fact]
    public void The_same_bytes_from_the_same_slot_are_the_same_publish()
    {
        var (store, state) = Store();

        var first = store.Begin(_game, Keys.Slot("savegame1"), "aaaa");
        var saves = state.Saves;
        var again = store.Begin(_game, Keys.Slot("savegame1"), "aaaa");

        Assert.Equal(first, again);
        Assert.Equal(saves, state.Saves);
    }

    [Fact]
    public void Other_bytes_from_the_same_slot_replace_the_unanswered_publish()
    {
        var (store, state) = Store();

        var first = store.Begin(_game, Keys.Slot("savegame1"), "aaaa");
        var second = store.Begin(_game, Keys.Slot("savegame1"), "bbbb");

        Assert.NotEqual(first.RequestId, second.RequestId);
        Assert.NotEqual(first.SavegameId, second.SavegameId);
        Assert.Equal(second, Assert.Single(state.Find(_game)!.SavegamePendingPublishes));
    }

    /// <summary>The whole reference: the same slot number in the game's other folder is another slot.</summary>
    [Fact]
    public void The_same_bytes_from_another_slot_are_another_publish()
    {
        var (store, state) = Store();

        var first = store.Begin(_game, Keys.Slot("savegame1"), "aaaa");
        var other = store.Begin(_game, Keys.Slot("savegame1", "client"), "aaaa");

        Assert.NotEqual(first.RequestId, other.RequestId);
        Assert.Equal(2, state.Find(_game)!.SavegamePendingPublishes.Count);
    }

    [Fact]
    public void A_completed_publish_is_not_repeated_and_completing_it_again_changes_nothing()
    {
        var (store, state) = Store();

        var first = store.Begin(_game, Keys.Slot("savegame1"), "aaaa");

        store.Complete(_game, first.RequestId);
        var saves = state.Saves;
        store.Complete(_game, first.RequestId);

        Assert.Equal(saves, state.Saves);
        Assert.NotEqual(first.RequestId, store.Begin(_game, Keys.Slot("savegame1"), "aaaa").RequestId);
    }

    [Fact]
    public void Beginning_a_publish_for_a_game_that_is_not_configured_is_refused()
    {
        var store = new SavegamePendingPublishes(new FakeGameState());

        Assert.Throws<InvalidOperationException>(() => store.Begin(_game, Keys.Slot("savegame1"), "aaaa"));
    }


    private (SavegamePendingPublishes Store, FakeGameState State) Store()
    {
        var state = new FakeGameState();

        state.Add(_game, new PersistedGame
        {
            GameAdapterId = new GameAdapterId("farmingSimulator", 1),
            Name = "Farming Simulator 25",
            AdapterLocalSettings = "{}"
        });

        return (new SavegamePendingPublishes(state), state);
    }
}
