using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ModsDude.Client.Core.Services;
public class RepoService(
    IReposClient repoClient,
    IGameAdapterIndex gameAdapterIndex)
{
    public delegate void RepoOfInterestChangedEventHandler(Guid repoIdOfInterest);
    public event RepoOfInterestChangedEventHandler? RepoOfInterestChanged;

    public ObservableCollection<RepoModel> Repos { get; } = [];


    public async Task RefreshRepos(CancellationToken cancellationToken)
    {
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        var repoModels = reposFromApi.Select(MapRepoModel);

        Repos.Clear();

        foreach (var repo in repoModels)
        {
            Repos.Add(repo);
        }
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

        await RefreshRepos(cancellationToken);

        OnRepoListChanged(repo.Id);
    }

    public async Task UpdateRepo(Guid id, string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        var request = new UpdateRepoRequest()
        {
            Name = name,
            AdapterConfiguration = baseSettings.Serialize()
        };
        try
        {
            await repoClient.UpdateRepoV1Async(id, request, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        await RefreshRepos(cancellationToken);
        
        OnRepoListChanged(id);
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

    private RepoModel MapRepoModel(RepoMembershipDto repoMembership)
    {
        return new RepoModel()
        {
            Id = repoMembership.Repo.Id,
            Name = repoMembership.Repo.Name,
            AdapterId = GameAdapterId.Parse(repoMembership.Repo.AdapterId),
            AdapterConfiguration = gameAdapterIndex
                .GetById(GameAdapterId.Parse(repoMembership.Repo.AdapterId))
                .DeserializeBaseSettings(repoMembership.Repo.AdapterConfiguration)
        };
    }
}
