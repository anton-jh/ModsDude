using ModsDude.Client.Core.Exceptions;
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
        RepoDto repo;

        var request = new CreateRepoRequest()
        {
            Name = name,
            AdapterId = adapterId,
            AdapterConfiguration = baseSettings.Serialize(),
        };
        try
        {
            repo = await repoClient.CreateRepoV1Async(request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

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

        RepoDto updated;

        try
        {
            updated = await repoClient.UpdateRepoV1Async(repo.Id, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

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

    public async Task DeleteRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.DeleteRepoV1Async(id, cancellationToken);

        if (FindRepo(id) is Repo removed)
        {
            Remove(removed);
        }
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
