using Microsoft.EntityFrameworkCore;
using ModsDude.WindowsClient.ApiClient.Generated;
using ModsDude.WindowsClient.Model.DbContexts;
using ModsDude.WindowsClient.Model.Exceptions;
using ModsDude.WindowsClient.Model.Models;
using System.Text.Json;

namespace ModsDude.WindowsClient.Model.Services;
public class RepoService(
    IReposClient repoClient,
    ApplicationDbContext dbContext,
    SessionService sessionService)
{
    public delegate void RepoListChangedEventHandler(Guid? repoIdOfInterest);
    public event RepoListChangedEventHandler? RepoListChanged;


    public async Task<IEnumerable<RepoModel>> GetRepos(CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSession(cancellationToken);
        var repos = await repoClient.GetMyReposV1Async(cancellationToken);
        var instances = await dbContext.LocalInstances
            .Where(x => x.UserId == session.UserId)
            .ToListAsync(cancellationToken);

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
            Name = name
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
