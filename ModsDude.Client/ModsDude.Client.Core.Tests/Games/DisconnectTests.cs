using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameAdapters;
using ModsDude.Client.Core.Tests.Services;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Games;

public sealed class DisconnectTests : IDisposable
{
    private static readonly GameIdentity _fs25 = new("farmingSimulator", "fs25");
    private static readonly GameIdentity _fs22 = new("farmingSimulator", "fs22");

    private readonly TempDirectory _root = new("disconnect");
    private readonly ResourceLeases _leases = new();
    private readonly IBaseGameAdapter _adapter = new FakeMultiTargetBaseGameAdapter();


    [Fact]
    public void A_game_holding_nothing_is_removed()
    {
        var state = StateWith(_fs25, _fs22);

        Assert.Equal(DisconnectOutcome.Removed, GameRepository.Disconnect(state, _fs25, new HashSet<Guid>()));
        Assert.Equal([_fs22], state.Games.Keys);
    }

    [Fact]
    public void A_game_holding_a_savegame_nobody_let_go_is_kept_untouched()
    {
        var state = StateWith(_fs25);
        var checkout = Checkout();

        state.Games[_fs25].SavegameCheckouts.Add(checkout);

        Assert.Equal(DisconnectOutcome.HoldsSavegame, GameRepository.Disconnect(state, _fs25, new HashSet<Guid>()));
        Assert.Equal(checkout, Assert.Single(state.Games[_fs25].SavegameCheckouts));
    }

    [Fact]
    public void A_game_holding_only_savegames_let_go_is_removed_with_their_records()
    {
        var state = StateWith(_fs25);
        var checkout = Checkout();

        state.Games[_fs25].SavegameCheckouts.Add(checkout);

        Assert.Equal(DisconnectOutcome.Removed, GameRepository.Disconnect(state, _fs25, new HashSet<Guid> { checkout.SavegameId }));
        Assert.Empty(state.Games);
    }

    /// <summary>One save let go does not take another along with it.</summary>
    [Fact]
    public void A_game_holding_one_savegame_let_go_and_one_not_is_kept_untouched()
    {
        var state = StateWith(_fs25);
        var letGo = Checkout();
        var kept = Checkout();

        state.Games[_fs25].SavegameCheckouts.AddRange([letGo, kept]);

        Assert.Equal(DisconnectOutcome.HoldsSavegame, GameRepository.Disconnect(state, _fs25, new HashSet<Guid> { letGo.SavegameId }));
        Assert.Equal([letGo, kept], state.Games[_fs25].SavegameCheckouts);
    }

    /// <summary>A second click, or another page having disconnected it a moment ago.</summary>
    [Fact]
    public void Disconnecting_a_game_that_is_already_gone_changes_nothing()
    {
        var state = StateWith(_fs22);

        Assert.Equal(DisconnectOutcome.AlreadyGone, GameRepository.Disconnect(state, _fs25, new HashSet<Guid>()));
        Assert.Equal([_fs22], state.Games.Keys);
    }

    [Fact]
    public void A_repeated_disconnect_reports_the_game_already_gone()
    {
        var games = Repository();
        var game = games.Create(_adapter, Settings());

        Assert.Equal(DisconnectOutcome.Removed, games.Delete(game, new HashSet<Guid>()));
        Assert.Equal(DisconnectOutcome.AlreadyGone, games.Delete(game, new HashSet<Guid>()));
        Assert.Empty(games.Games);
    }

    [Fact]
    public void A_game_is_left_connected_while_something_works_in_its_folders()
    {
        var games = Repository();
        var game = games.Create(_adapter, Settings());

        using (_leases.TryAcquireExclusive(game.TargetRefs.Select(ResourceKeys.Target), "Applying"))
        {
            Assert.Equal(DisconnectOutcome.Busy, games.Delete(game, new HashSet<Guid>()));
            Assert.Same(game, Assert.Single(games.Games));
        }

        Assert.Equal(DisconnectOutcome.Removed, games.Delete(game, new HashSet<Guid>()));
    }

    [Fact]
    public void A_game_holding_a_savegame_nobody_let_go_stays_connected()
    {
        var games = Repository();
        var game = games.Create(_adapter, Settings());

        game.PersistedModel.SavegameCheckouts.Add(Checkout());

        Assert.Equal(DisconnectOutcome.HoldsSavegame, games.Delete(game, new HashSet<Guid>()));
        Assert.Same(game, Assert.Single(games.Games));
    }


    public void Dispose()
    {
        _root.Dispose();
    }


    private GameRepository Repository()
        => new(new MemoryStateStore(), new SyncManifestStore(Path.Combine(_root.Path, "manifests")), _leases);

    private FakeMultiTargetSettings Settings()
        => new() { SoloModFolder = Path.Combine(_root.Path, "mods"), SoloSavegameFolder = Path.Combine(_root.Path, "saves") };

    private static SavegameCheckoutBinding Checkout()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new SavegameSlotRef(new TargetKey("fs"), new SavegameSlotId("savegame1")),
            4,
            "aaaa",
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

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
