using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class RepoRepository(
    IReposClient repoClient,
    IGameAdapterIndex gameAdapterIndex,
    GameRepository gameRepository)
    : IUserScopedState
{
    public delegate void RepoCreatedEventHandler(Guid repoId);

    /// <summary>
    /// Raised for a repo that did not exist a moment ago, so the shell can navigate to it. Renames
    /// need no equivalent: the model is updated in place and the menu entry follows it.
    /// </summary>
    public event RepoCreatedEventHandler? RepoCreated;

    public ObservableCollection<Repo> Repos { get; } = [];

    /// <summary>
    /// Whether this account's repos have been read at least once.
    /// </summary>
    /// <remarks>
    /// <b>An empty list means two different things and something has to tell them apart.</b> Before
    /// the first read it means "not asked yet"; after one it means this account is in no repos. The
    /// drift notice is the caller that cares: a game whose repo it cannot find is either a shell
    /// that started three seconds ago or a game this account can do nothing about, and those get
    /// opposite sentences. Set on success only - a failed read has established nothing.
    /// </remarks>
    public bool HasLoaded { get; private set; }

    /// <summary>
    /// What the last background check found on the server that <see cref="Repos"/> does not show
    /// yet, or null where it found nothing. Cleared by any refresh, which is what brings it in.
    /// </summary>
    public RemoteChanges? PendingChanges { get; private set; }

    /// <summary>Raised when <see cref="PendingChanges"/> is set or cleared.</summary>
    public event EventHandler? PendingChangesChanged;


    /// <summary>
    /// Asks the server whether the list has changed, and records the answer in
    /// <see cref="PendingChanges"/> without touching <see cref="Repos"/>.
    /// </summary>
    /// <remarks>
    /// <b>Discarded where the list moved while the question was out.</b> Creating, joining or
    /// renaming a repo on this machine changes the list between the request and the answer, and
    /// comparing an answer from before that with a list from after it would report this client's
    /// own change as somebody else's.
    /// </remarks>
    public async Task CheckForChanges(CancellationToken cancellationToken)
    {
        // Nothing to compare against yet, and before sign-in there is nobody to ask for.
        if (HasLoaded is false)
        {
            return;
        }

        var before = Snapshot();
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        if (HasLoaded is false || before.SequenceEqual(Snapshot()) is false)
        {
            return;
        }

        SetPendingChanges(RepoListChanges.Between(before, reposFromApi));
    }

    public async Task RefreshRepos(CancellationToken cancellationToken)
    {
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        var byId = reposFromApi.ToDictionary(x => x.Repo.Id);

        // Reconciled rather than rebuilt. Clearing would discard every menu entry and every open
        // page built from these repos, and each Repo holds a synchronizer subscribed to the
        // machine's game list that has to be disposed exactly when the repo really goes away.
        for (var i = Repos.Count - 1; i >= 0; i--)
        {
            if (!byId.ContainsKey(Repos[i].Id))
            {
                Remove(Repos[i]);
            }
        }

        foreach (var dto in reposFromApi)
        {
            if (FindRepo(dto.Repo.Id) is Repo existing)
            {
                existing.Apply(dto);
            }
            else
            {
                Repos.Add(MapRepoModel(dto));
            }
        }

        // Last, so that a listener woken by the collection changing above sees the list before it
        // sees the flag saying the list is complete.
        HasLoaded = true;

        SetPendingChanges(null);
    }

    public async Task CreateRepo(string name, string adapterId, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        var request = new CreateRepoRequest()
        {
            Name = name,
            AdapterId = adapterId,
            AdapterConfiguration = baseSettings.Serialize(),
        };

        // No name to lose the race for: repo names are not unique, so the only reason this could
        // come back a failure is one the error reporter can say better than a catch here.
        var repo = await repoClient.CreateRepoV1Async(request, cancellationToken);

        // The creator is the repo's first Admin, so the response carries everything the list needs.
        Repos.Add(MapRepoModel(new RepoMembershipDto()
        {
            Repo = repo,
            MembershipLevel = RepoMembershipLevel.Admin
        }));

        RepoCreated?.Invoke(repo.Id);
    }

    /// <summary>
    /// Puts a repo the user has just joined into the list, so the shell can navigate to it without
    /// waiting for a refresh. Ignored where the repo is already there - redeeming a code twice is
    /// allowed, and must not produce two of the same repo.
    /// </summary>
    public void AddJoinedRepo(RepoMembershipDto membership)
    {
        if (FindRepo(membership.Repo.Id) is not null)
        {
            return;
        }

        Repos.Add(MapRepoModel(membership));
        RepoCreated?.Invoke(membership.Repo.Id);
    }

    public async Task Update(Repo repo, string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        var request = new UpdateRepoRequest()
        {
            Name = name,
            AdapterConfiguration = baseSettings.Serialize()
        };

        var updated = await repoClient.UpdateRepoV1Async(repo.Id, request, cancellationToken);

        repo.Apply(updated);
    }

    /// <summary>
    /// Every repo here came out of one account's memberships, so a different user starts from an
    /// empty list rather than from one the next refresh would have to contradict.
    /// </summary>
    public void ClearUserState()
    {
        for (var i = Repos.Count - 1; i >= 0; i--)
        {
            Remove(Repos[i]);
        }

        // Back to "not asked yet", which is what this now is: the new account's repos are unknown
        // rather than known to be none, and a notice reading the difference must not answer for the
        // account that just left.
        HasLoaded = false;

        SetPendingChanges(null);
    }

    /// <summary>
    /// Permanently deletes an archived repo. Refused by the server for one that is still live, and
    /// for one that still holds mods.
    /// </summary>
    public async Task DeleteRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.DeleteRepoV1Async(id, cancellationToken);

        if (FindRepo(id) is Repo removed)
        {
            Remove(removed);
        }
    }

    /// <summary>
    /// Puts a repo away, for everybody. Archiving is repo state rather than membership state, so
    /// this is not a personal "hide it from me" - it leaves every member's sidebar at once.
    /// </summary>
    public async Task ArchiveRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.ArchiveRepoV1Async(id, cancellationToken);

        if (FindRepo(id) is Repo archived)
        {
            Remove(archived);
        }
    }

    /// <summary>
    /// Brings one back, under the name it went away with. Unlike restoring a profile or a savegame
    /// this takes no name and cannot fail on one: repo names are not unique, so an archived repo
    /// never gave its name up for anybody else to take.
    /// </summary>
    public async Task RestoreRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.RestoreRepoV1Async(id, cancellationToken);

        // Refetched rather than constructed here: a Repo wraps a membership, hydrates an adapter and
        // holds a collection synchronizer, and half-building one from a restore response is how the
        // two get to disagree.
        await RefreshRepos(cancellationToken);
    }

    /// <summary>
    /// The archived repos this user is a member of. Read on demand: the Archive is a page somebody
    /// visits, not part of the shell.
    /// </summary>
    public async Task<IReadOnlyList<RepoMembershipDto>> GetArchivedRepos(CancellationToken cancellationToken)
    {
        return [.. await repoClient.GetArchivedReposV1Async(cancellationToken)];
    }


    private Repo? FindRepo(Guid id)
    {
        return Repos.FirstOrDefault(x => x.Id == id);
    }

    private List<RepoListEntry> Snapshot()
    {
        return [.. Repos.Select(x => x.ToListEntry())];
    }

    private void SetPendingChanges(RemoteChanges? changes)
    {
        if (PendingChanges is null && changes is null)
        {
            return;
        }

        PendingChanges = changes;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(Repo repo)
    {
        Repos.Remove(repo);
        repo.Dispose();
    }

    private Repo MapRepoModel(RepoMembershipDto repoMembership)
    {
        return new Repo(repoMembership, gameAdapterIndex, this, gameRepository);
    }
}
