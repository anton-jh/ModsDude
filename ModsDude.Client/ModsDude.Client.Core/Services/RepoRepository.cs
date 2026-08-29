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
{
    public delegate void RepoOfInterestChangedEventHandler(Guid repoIdOfInterest);
    public event RepoOfInterestChangedEventHandler? RepoOfInterestChanged;

    public ObservableCollection<Repo> Repos { get; } = [];


    public async Task RefreshRepos(CancellationToken cancellationToken)
    {
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        var repoModels = reposFromApi.Select(MapRepoModel);

        // Each repo holds a synchronizer subscribed to the machine's instance list, which outlives
        // every refresh.
        foreach (var repo in Repos)
        {
            repo.Dispose();
        }

        Repos.Clear();

        foreach (var repo in repoModels)
        {
            Repos.Add(repo);
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

        await RefreshRepos(cancellationToken);

        OnRepoListChanged(repo.Id);
    }

    public async Task Update(Repo repo, CancellationToken cancellationToken)
    {
        var request = new UpdateRepoRequest()
        {
            Name = repo.Name,
            AdapterConfiguration = repo.Adapter.BaseSettings.Serialize()
        };
        try
        {
            await repoClient.UpdateRepoV1Async(repo.Id, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        await RefreshRepos(cancellationToken);
        
        OnRepoListChanged(repo.Id);
    }

    public async Task DeleteRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.DeleteRepoV1Async(id, cancellationToken);

        await RefreshRepos(cancellationToken);
    }


    private void OnRepoListChanged(Guid idOfInterest)
    {
        RepoOfInterestChanged?.Invoke(idOfInterest);
    }

    private Repo MapRepoModel(RepoMembershipDto repoMembership)
    {
        return new Repo(repoMembership, gameAdapterIndex, this, localInstanceRepository);
    }
}
