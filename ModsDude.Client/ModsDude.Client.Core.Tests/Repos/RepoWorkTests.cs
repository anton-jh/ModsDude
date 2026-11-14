using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameAdapters;
using ModsDude.Client.Core.Tests.Services;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Repos;

public sealed class RepoWorkTests : IDisposable
{
    private static readonly Guid _repo = Guid.NewGuid();
    private static readonly Guid _otherRepo = Guid.NewGuid();
    private static readonly Guid _profile = Guid.NewGuid();
    private static readonly Guid _savegame = Guid.NewGuid();

    private readonly TempDirectory _root = new("repo-work");
    private readonly GameRepository _games;
    private readonly Game _game;


    public RepoWorkTests()
    {
        _games = new GameRepository(new MemoryStateStore(), new SyncManifestStore(_root.Combine("manifests")), new ResourceLeases());
        _game = _games.Create(new FakeMultiTargetBaseGameAdapter(), new FakeMultiTargetSettings { SoloModFolder = _root.Combine("mods") });
    }


    [Fact]
    public void An_import_a_save_a_savegame_operation_and_an_apply_of_its_profile_all_count_as_work_for_the_repo()
    {
        _games.SetActiveProfile(_game, new ActiveProfile(_repo, _profile));

        var keys = RepoWork.KeysOf(_repo, [_profile], [_savegame], _games.Games);

        Assert.Contains(ResourceKeys.Repo(_repo), keys);
        Assert.Contains(ResourceKeys.Profile(_profile), keys);
        Assert.Contains(ResourceKeys.Savegame(_savegame), keys);
        Assert.Contains(ResourceKeys.Target(Assert.Single(_game.TargetRefs)), keys);
    }

    [Fact]
    public void A_game_following_another_repos_profile_is_not_work_for_this_one()
    {
        _games.SetActiveProfile(_game, new ActiveProfile(_otherRepo, Guid.NewGuid()));

        var keys = RepoWork.KeysOf(_repo, [], [], _games.Games);

        Assert.Equal([ResourceKeys.Repo(_repo)], keys);
    }

    [Fact]
    public void Nothing_can_be_claimed_while_any_part_of_it_is_held()
    {
        var leases = new ResourceLeases();
        var keys = RepoWork.KeysOf(_repo, [_profile], [_savegame], _games.Games);

        using (leases.TryAcquireExclusive(ResourceKeys.Savegame(_savegame), "Checking in"))
        {
            Assert.Null(leases.TryAcquireExclusive(keys, "Leaving"));
            Assert.False(leases.IsHeld(ResourceKeys.Repo(_repo)));
        }

        using var claim = leases.TryAcquireExclusive(keys, "Leaving");

        Assert.NotNull(claim);
    }


    public void Dispose()
    {
        _root.Dispose();
    }
}
