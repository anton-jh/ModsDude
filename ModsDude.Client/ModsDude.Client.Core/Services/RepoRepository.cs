using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class RepoRepository(
    IReposClient repoClient,
    IGameAdapterIndex gameAdapterIndex,
    LocalInstanceRepository localInstanceRepository)
    : IUserScopedState
{
    public delegate void RepoCreatedEventHandler(Guid repoId);

    /// <summary>
    /// Raised for a repo that did not exist a moment ago, so the shell can navigate to it. Renames
    /// need no equivalent: the model is updated in place and the menu entry follows it.
    /// </summary>
    public event RepoCreatedEventHandler? RepoCreated;

    public ObservableCollection<Repo> Repos { get; } = [];


    public async Task RefreshRepos(CancellationToken cancellationToken)
    {
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        var byId = reposFromApi.ToDictionary(x => x.Repo.Id);

        // Reconciled rather than rebuilt. Clearing would discard every menu entry and every open
        // page built from these repos, and each Repo holds a synchronizer subscribed to the
        // machine's instance list that has to be disposed exactly when the repo really goes away.
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

    private void Remove(Repo repo)
    {
        Repos.Remove(repo);
        repo.Dispose();
    }

    private Repo MapRepoModel(RepoMembershipDto repoMembership)
    {
        return new Repo(repoMembership, gameAdapterIndex, this, localInstanceRepository);
    }
}
