using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Models;

public class Repo
    : IDisposable
{
    private readonly RepoRepository _repoService;
    private readonly ObservableCollectionSynchronizer<LocalInstance, PersistedLocalInstance, Guid> _instancesSynchronizer;


    public Repo(
        RepoMembershipDto repoMembershipDto,
        IGameAdapterIndex gameAdapterIndex,
        RepoRepository repoService,
        LocalInstanceRepository localInstanceRepository)
    {
        Adapter = gameAdapterIndex.GetById(GameAdapterId.Parse(repoMembershipDto.Repo.AdapterId)).WithBaseSettings(repoMembershipDto.Repo.AdapterConfiguration);
        _repoService = repoService;
        Id = repoMembershipDto.Repo.Id;
        Name = repoMembershipDto.Repo.Name;
        MembershipLevel = repoMembershipDto.MembershipLevel;

        LocalInstances = new(localInstanceRepository.GetByRepoId(Id)
            .Select(x => new LocalInstance(Adapter, this, x)));

        _instancesSynchronizer = new(
            source: LocalInstances,
            target: localInstanceRepository.GetByRepoId(Id),
            factory: x => x.PersistedModel,
            keySelectorExpression: x => Id,
            targetAlreadyInitialized: true);
    }


    public Guid Id { get; }
    public string Name { get; private set; }
    public RepoMembershipLevel MembershipLevel { get; }
    public ObservableCollection<LocalInstance> LocalInstances { get; }
    public IBaseGameAdapter Adapter { get; private set; }

    // TODO: Profiles


    public Task Update(string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        Name = name;
        Adapter = Adapter.WithBaseSettings(baseSettings);
        return _repoService.Update(this, cancellationToken);
    }

    public void CreateLocalInstance(string name, DynamicForm instanceSettings)
    {
        var instance = new LocalInstance(Adapter, this, name, instanceSettings);
        LocalInstances.Add(instance);
    }

    public void DeleteLocalInstance(LocalInstance instance)
    {
        LocalInstances.Remove(instance);
    }

    public void Dispose()
    {
        _instancesSynchronizer.Dispose();
    }
}
