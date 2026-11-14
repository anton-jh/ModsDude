using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameAdapters;
using ModsDude.Client.Core.Tests.Services;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Games;

/// <summary>
/// A game's folders move only while nothing works in them and no savegame is held in them.
/// </summary>
public sealed class GameFolderChangeTests : IDisposable
{
    private readonly TempDirectory _root = new("game-folder-change");
    private readonly ResourceLeases _leases = new();
    private readonly IBaseGameAdapter _adapter = new FakeMultiTargetBaseGameAdapter();
    private readonly GameRepository _games;
    private readonly Game _game;


    public GameFolderChangeTests()
    {
        _games = new GameRepository(new MemoryStateStore(), new SyncManifestStore(Path.Combine(_root.Path, "manifests")), _leases);
        _game = _games.Create(_adapter, Settings("mods", "saves"));
    }


    [Fact]
    public void Settings_change_while_nothing_works_in_the_folders()
    {
        _games.Update(_game, _adapter, Settings("moved", "saves"));

        Assert.Equal(Folder("moved"), Assert.Single(_game.Targets).ModFolder);
    }

    [Fact]
    public void Settings_are_refused_while_an_apply_holds_a_mod_folder()
    {
        using var apply = _leases.TryAcquireExclusive(_game.TargetRefs.Select(ResourceKeys.Target), "Applying");

        Assert.Throws<UserFriendlyException>(() => _games.Update(_game, _adapter, Settings("moved", "saves")));
        Assert.Equal(Folder("mods"), Assert.Single(_game.Targets).ModFolder);
    }

    [Fact]
    public void Settings_are_refused_while_savegame_work_holds_a_savegame_folder()
    {
        using var checkIn = _leases.TryAcquireExclusive(ResourceKeys.SavegameFolder(_game.Identity, FakeMultiTargetSettings.Solo), "Checking in");

        Assert.Throws<UserFriendlyException>(() => _games.Update(_game, _adapter, Settings("mods", "moved")));
        Assert.Equal(Settings("mods", "saves").SoloSavegameFolder, ((FakeMultiTargetSettings)_game.GetLocalSettings(_adapter)).SoloSavegameFolder);
    }

    [Fact]
    public void Settings_are_refused_while_the_game_holds_a_savegame()
    {
        Hold();

        Assert.Throws<UserFriendlyException>(() => _games.Update(_game, _adapter, Settings("moved", "saves")));
        Assert.Equal(Folder("mods"), Assert.Single(_game.Targets).ModFolder);
    }

    [Fact]
    public void Folders_that_did_not_move_are_left_alone()
    {
        Assert.Equal(TargetRefresh.Unchanged, _games.RefreshTargets(_game, _adapter));
    }

    [Fact]
    public void Folders_that_moved_are_written_down()
    {
        Move();

        Assert.Equal(TargetRefresh.Updated, _games.RefreshTargets(_game, _adapter));
        Assert.Equal(Folder("mods"), Assert.Single(_game.Targets).ModFolder);
    }

    [Fact]
    public void Folders_that_moved_under_running_work_are_left_as_they_were_until_it_finishes()
    {
        Move();

        using (_leases.TryAcquireExclusive(_game.TargetRefs.Select(ResourceKeys.Target), "Applying"))
        {
            Assert.Equal(TargetRefresh.Busy, _games.RefreshTargets(_game, _adapter));
            Assert.Equal(Folder("old"), Assert.Single(_game.Targets).ModFolder);
        }

        Assert.Equal(TargetRefresh.Updated, _games.RefreshTargets(_game, _adapter));
    }

    [Fact]
    public void Folders_that_moved_while_a_savegame_is_held_are_left_as_they_were()
    {
        Move();
        Hold();

        Assert.Equal(TargetRefresh.HoldsSavegame, _games.RefreshTargets(_game, _adapter));
        Assert.Equal(Folder("old"), Assert.Single(_game.Targets).ModFolder);
    }


    public void Dispose()
    {
        _root.Dispose();
    }


    private string Folder(string name) => Path.Combine(_root.Path, name);

    private FakeMultiTargetSettings Settings(string mods, string saves)
        => new() { SoloModFolder = Folder(mods), SoloSavegameFolder = Folder(saves) };

    /// <summary>The written-down folder is somewhere the settings no longer put it, as when the game's own settings moved it.</summary>
    private void Move()
        => _game.PersistedModel.Targets = [new PersistedModTarget(FakeMultiTargetSettings.Solo, Folder("old"))];

    private void Hold()
        => _game.PersistedModel.SavegameCheckouts.Add(new SavegameCheckoutBinding(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new SavegameSlotRef(FakeMultiTargetSettings.Solo, new SavegameSlotId("savegame1")),
            4,
            "aaaa",
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)));
}
