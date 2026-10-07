using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameAdapters;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Repos;

public class RepoStoreTests
{
    [Fact]
    public void Nothing_is_gone_before_the_list_has_been_read()
    {
        using var fixture = new Fixture();

        Assert.False(fixture.Repos.IsGone(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_repo_missing_from_the_list_is_gone_and_one_in_it_is_not()
    {
        using var fixture = new Fixture();
        var mine = fixture.Server.Add();

        await fixture.Repos.RefreshRepos(CancellationToken.None);

        Assert.False(fixture.Repos.IsGone(mine));
        Assert.True(fixture.Repos.IsGone(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_repo_that_leaves_the_list_on_a_refresh_is_gone()
    {
        using var fixture = new Fixture();
        var left = fixture.Server.Add();

        await fixture.Repos.RefreshRepos(CancellationToken.None);
        fixture.Server.Memberships.Clear();
        await fixture.Repos.RefreshRepos(CancellationToken.None);

        Assert.True(fixture.Repos.IsGone(left));
    }

    [Fact]
    public async Task A_repo_joined_while_the_list_was_being_read_is_listed_once()
    {
        using var fixture = new Fixture();
        var gate = fixture.Server.Hold();

        var refresh = fixture.Repos.RefreshRepos(CancellationToken.None);
        var joined = fixture.Server.Add();
        await fixture.Repos.Join(_ => Task.FromResult(fixture.Server.Memberships.Single()), CancellationToken.None);
        gate.SetResult();
        await refresh;

        Assert.Equal(joined, Assert.Single(fixture.Repos.Repos).Id);
    }

    [Fact]
    public async Task Joining_a_repo_already_listed_keeps_one_of_it()
    {
        using var fixture = new Fixture();
        fixture.Server.Add();
        await fixture.Repos.RefreshRepos(CancellationToken.None);

        await fixture.Repos.Join(_ => Task.FromResult(fixture.Server.Memberships.Single()), CancellationToken.None);

        Assert.Single(fixture.Repos.Repos);
    }

    [Fact]
    public async Task A_read_still_out_when_the_user_changes_lists_nothing_of_theirs()
    {
        using var fixture = new Fixture();
        fixture.Server.Add();
        var gate = fixture.Server.Hold();

        var refresh = fixture.Repos.RefreshRepos(CancellationToken.None);
        fixture.Repos.ClearUserState();
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Empty(fixture.Repos.Repos);
        Assert.False(fixture.Repos.HasLoaded);
    }

    [Fact]
    public async Task A_deleted_repo_is_gone_at_once()
    {
        using var fixture = new Fixture();
        var deleted = fixture.Server.Add();

        await fixture.Repos.RefreshRepos(CancellationToken.None);
        await fixture.Repos.DeleteRepo(deleted, CancellationToken.None);

        Assert.True(fixture.Repos.IsGone(deleted));
    }

    /// <summary>The next account's repos are unknown rather than known to be none.</summary>
    [Fact]
    public async Task Nothing_is_gone_after_the_user_changes()
    {
        using var fixture = new Fixture();

        await fixture.Repos.RefreshRepos(CancellationToken.None);
        fixture.Repos.ClearUserState();

        Assert.False(fixture.Repos.IsGone(Guid.NewGuid()));
    }


    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory _manifests = new("repo-repository-manifests");

        public Fixture()
        {
            var games = new GameRepository(new MemoryStateStore(), new SyncManifestStore(_manifests.Path), new ResourceLeases());

            Repos = new RepoStore(
                Server,
                new GameAdapterIndex([new FakeMultiTargetGameAdapter()]),
                games,
                InlineStoreDispatcher.Instance,
                NullLogger<RepoStore>.Instance);
        }


        public FakeReposServer Server { get; } = new();

        public RepoStore Repos { get; }


        public void Dispose()
        {
            foreach (var repo in Repos.Repos)
            {
                repo.Dispose();
            }

            _manifests.Dispose();
        }
    }

    private sealed class MemoryStateStore : IStateStore
    {
        private readonly LocalState _state = new();

        public TResult Read<TResult>(Func<LocalState, TResult> read) => read(_state);

        public void Update(Action<LocalState> update) => update(_state);

        public bool UpdateIf(Func<LocalState, bool> update) => update(_state);
    }

    private sealed class FakeReposServer : IReposClient
    {
        public List<RepoMembershipDto> Memberships { get; } = [];


        public Guid Add()
        {
            var id = Guid.NewGuid();

            Memberships.Add(new RepoMembershipDto
            {
                MembershipLevel = RepoMembershipLevel.Admin,
                Repo = new RepoDto
                {
                    Id = id,
                    Name = "Repo",
                    Tag = "0001",
                    AdapterId = new FakeMultiTargetGameAdapter().Id.ToString(),
                    AdapterConfiguration = "{}"
                }
            });

            return id;
        }

        private TaskCompletionSource? _held;

        /// <summary>Holds the next read until the test releases it, answering with the list as it was when asked.</summary>
        public TaskCompletionSource Hold()
            => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ICollection<RepoMembershipDto>> GetMyReposV1Async(CancellationToken cancellationToken = default)
        {
            List<RepoMembershipDto> answer = [.. Memberships];

            if (_held is { } held)
            {
                _held = null;
                await held.Task.WaitAsync(cancellationToken);
            }

            return answer;
        }

        public Task DeleteRepoV1Async(Guid repoId, CancellationToken cancellationToken = default)
        {
            Memberships.RemoveAll(x => x.Repo.Id == repoId);

            return Task.CompletedTask;
        }


        public Task<RepoDto> CreateRepoV1Async(CreateRepoRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ICollection<RepoMembershipDto>> GetArchivedReposV1Async(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RepoDetailsDto> GetRepoDetailsV1Async(Guid repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RepoDto> UpdateRepoV1Async(Guid repoId, UpdateRepoRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ArchiveRepoV1Async(Guid repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RestoreRepoV1Async(Guid repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
