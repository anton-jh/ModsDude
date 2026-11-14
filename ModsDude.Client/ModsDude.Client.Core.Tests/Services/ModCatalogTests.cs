using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameAdapters;
using ModsDude.Client.Core.Tests.Import;
using ModsDude.Client.Core.Tests.Repos;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Services;

public sealed class ModCatalogTests : IDisposable
{
    private readonly TempDirectory _manifests = new("mod-catalog-manifests");
    private readonly FakeModsDudeServer _server = new();
    private readonly Repo _repo;
    private readonly ModCatalog _catalog;


    public ModCatalogTests()
    {
        var games = new GameRepository(new MemoryStateStore(), new SyncManifestStore(_manifests.Path), new ResourceLeases());

        _repo = new Repo(
            new RepoMembershipDto
            {
                MembershipLevel = RepoMembershipLevel.Admin,
                Repo = new RepoDto
                {
                    Id = _server.RepoId,
                    Name = "Repo",
                    Tag = "0001",
                    AdapterId = new FakeMultiTargetGameAdapter().Id.ToString(),
                    AdapterConfiguration = "{}"
                }
            },
            new GameAdapterIndex([new FakeMultiTargetGameAdapter()]),
            new FakeRepoStore(),
            games);

        var modStore = new ModStore(_server, InlineStoreDispatcher.Instance, NullLogger<ModStore>.Instance);

        _catalog = new ModCatalog.Factory(_server, modStore, NullLogger<ModCatalog>.Instance).Create(_repo);
    }


    [Fact]
    public async Task A_mod_list_read_that_failed_is_read_again_on_the_next_ask()
    {
        var failures = 1;
        _server.BeforeGetMods = () => failures-- > 0 ? throw new HttpRequestException("blip") : Task.CompletedTask;

        await Assert.ThrowsAsync<HttpRequestException>(() => _catalog.GetAsync(CancellationToken.None));

        var snapshot = await _catalog.GetAsync(CancellationToken.None);

        Assert.Empty(snapshot.Versions);
    }

    [Fact]
    public async Task A_usage_read_that_failed_is_read_again_on_the_next_ask()
    {
        var failures = 1;
        _server.BeforeGetUsage = () => failures-- > 0 ? throw new HttpRequestException("blip") : Task.CompletedTask;

        await Assert.ThrowsAsync<HttpRequestException>(() => _catalog.GetAsync(CancellationToken.None));

        var snapshot = await _catalog.GetAsync(CancellationToken.None);

        Assert.Empty(snapshot.Versions);
    }


    public void Dispose()
    {
        _catalog.Dispose();
        _repo.Dispose();
        _manifests.Dispose();
    }
}
