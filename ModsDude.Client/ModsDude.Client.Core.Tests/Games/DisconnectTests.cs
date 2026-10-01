using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Games;

public class DisconnectTests
{
    private static readonly GameIdentity _fs25 = new("farmingSimulator", "fs25");
    private static readonly GameIdentity _fs22 = new("farmingSimulator", "fs22");


    [Fact]
    public void A_game_holding_nothing_is_removed()
    {
        var state = StateWith(_fs25, _fs22);

        Assert.Equal(GameRepository.DisconnectOutcome.Removed, GameRepository.Disconnect(state, _fs25));
        Assert.Equal([_fs22], state.Games.Keys);
    }

    [Fact]
    public void A_game_holding_a_savegame_is_kept_untouched()
    {
        var state = StateWith(_fs25);
        var checkout = new SavegameCheckoutBinding(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new SavegameSlotRef(new TargetKey("fs"), new SavegameSlotId("savegame1")),
            4,
            "aaaa",
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

        state.Games[_fs25].SavegameCheckouts.Add(checkout);

        Assert.Equal(GameRepository.DisconnectOutcome.HoldsSavegame, GameRepository.Disconnect(state, _fs25));
        Assert.Equal(checkout, Assert.Single(state.Games[_fs25].SavegameCheckouts));
    }

    /// <summary>A second click, or another repo of the same game having disconnected it a moment ago.</summary>
    [Fact]
    public void Disconnecting_a_game_that_is_already_gone_changes_nothing()
    {
        var state = StateWith(_fs22);

        Assert.Equal(GameRepository.DisconnectOutcome.AlreadyGone, GameRepository.Disconnect(state, _fs25));
        Assert.Equal([_fs22], state.Games.Keys);
    }


    private static LocalState StateWith(params GameIdentity[] identities)
    {
        var state = new LocalState();

        foreach (var identity in identities)
        {
            state.Games[identity] = new PersistedGame
            {
                GameAdapterId = new GameAdapterId(identity.AdapterId, 1),
                Name = identity.ToString(),
                AdapterLocalSettings = "{}",
                Targets = []
            };
        }

        return state;
    }
}
