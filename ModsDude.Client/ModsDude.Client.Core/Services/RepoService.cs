using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Text.Json;

namespace ModsDude.Client.Core.Services;
public class RepoService(
    IReposClient repoClient)
{
    public delegate void RepoListChangedEventHandler(Guid? repoIdOfInterest);
    public event RepoListChangedEventHandler? RepoListChanged;


    public async Task<IEnumerable<RepoModel>> GetRepos(CancellationToken cancellationToken)
    {
        var repos = await repoClient.GetMyReposV1Async(cancellationToken);
        var instances = new List<ILocalInstance>(); // TODO

        var combinedRepos = repos.Select(x => new RepoModel()
        {
            Id = x.Repo.Id,
            Name = x.Repo.Name,
            AdapterId = x.Repo.AdapterId,
            AdapterConfiguration = x.Repo.AdapterConfiguration,
            LocalInstances = instances.Where(i => i.RepoId == x.Repo.Id).ToList()
        });

        return combinedRepos;
    }

    public async Task CreateRepo(string name, string adapterId, object adapterConfiguration, CancellationToken cancellationToken)
    {
        RepoDto repo;

        var serializedAdapterConfiguration = JsonSerializer.Serialize(adapterConfiguration);

        var request = new CreateRepoRequest()
        {
            Name = name,
            AdapterId = adapterId,
            AdapterConfiguration = serializedAdapterConfiguration,
        };
        try
        {
            repo = await repoClient.CreateRepoV1Async(request, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }
        OnRepoListChanged(repo.Id);
    }

    public async Task UpdateRepo(Guid id, string name, CancellationToken cancellationToken)
    {
        var request = new UpdateRepoRequest()
        {
            Name = name,
            AdapterConfiguration = "" // TEMP
        };
        try
        {
            await repoClient.UpdateRepoV1Async(id, request, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }
        OnRepoListChanged(id);
    }

    public async Task DeleteRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.DeleteRepoV1Async(id, cancellationToken);

        OnRepoListChanged(null);
    }


    private void OnRepoListChanged(Guid? idOfInterest)
    {
        RepoListChanged?.Invoke(idOfInterest);
    }
}
