using ModsDude.Client.Core.Persistence;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public class LocalInstanceRepository(
    StateStore store)
{
    private readonly LocalState _state = store.Get();
    private readonly HashSet<ObservableCollection<PersistedLocalInstance>> _observedCollections = new();


    public ObservableCollection<PersistedLocalInstance> GetByRepoId(Guid repoId)
    {
        var instances = _state.GetRepoStateById(repoId).LocalInstances;
        if (_observedCollections.Add(instances))
        {
            instances.CollectionChanged += (_, _) => store.Save();
        }
        return instances;
    }
}
