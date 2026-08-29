using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Models;

public class Repo
    : IDisposable
{
    private readonly RepoRepository _repoService;
    private readonly ObservableCollectionSynchronizer<LocalInstance, LocalInstance, string> _instancesSynchronizer;


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

        LocalInstances = [];

        // Offered, not owned: an instance belongs to a game, so every repo targeting that game
        // lists the same instances.
        _instancesSynchronizer = new(
            source: localInstanceRepository.Instances,
            target: LocalInstances,
            factory: x => x,
            keySelectorExpression: x => x.Name,
            filter: x => x.Scope == Scope);
    }


    public Guid Id { get; }
    public string Name { get; private set; }
    public RepoMembershipLevel MembershipLevel { get; }
    public ObservableCollection<LocalInstance> LocalInstances { get; }
    public IBaseGameAdapter Adapter { get; private set; }
    public InstanceScope Scope => Adapter.Scope;

    // TODO: Profiles


    public Task Update(string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        Name = name;
        Adapter = Adapter.WithBaseSettings(baseSettings);
        return _repoService.Update(this, cancellationToken);
    }

    public void Dispose()
    {
        _instancesSynchronizer.Dispose();
    }
}
