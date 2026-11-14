using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Tests.Repos;

/// <summary>The repo list, which a poll only ever reads again.</summary>
internal sealed class FakeRepoStore : IRepoStore
{
    public event Action<Guid>? RepoCreated { add { } remove { } }

    public ObservableCollection<Repo> Repos { get; } = [];
    public bool HasLoaded => true;
    public int Refreshes { get; private set; }

    public Task RefreshRepos(CancellationToken cancellationToken)
    {
        Refreshes++;

        return Task.CompletedTask;
    }

    public bool IsGone(Guid repoId) => false;
    public void ClearUserState() { }

    public Task CreateRepo(Guid requestId, string name, string adapterId, DynamicForm baseSettings, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<RepoMembershipDto> Join(Func<CancellationToken, Task<RepoMembershipDto>> redeem, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task Update(Repo repo, string name, DynamicForm baseSettings, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteRepo(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ArchiveRepo(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task RestoreRepo(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<RepoMembershipDto>> GetArchivedRepos(CancellationToken cancellationToken) => throw new NotSupportedException();
}
