using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public class LocalInstanceService(
    StateStore store,
    IGameAdapterIndex gameAdapterIndex)
{
    private readonly LocalState _state = store.Get();


    public ObservableCollection<LocalInstance> GetByRepoId(Guid repoId)
    {
        return _state.GetRepoStateById(repoId).LocalInstances;
    }

    public LocalInstance Create(RepoModel repo, string name, DynamicForm instanceSettings)
    {
        var instance = new LocalInstance(repo.Id, name, gameAdapterIndex.GetById(repo.AdapterId).SerializeInstanceSettings(instanceSettings));

        GetByRepoId(repo.Id).Add(instance);

        store.Save();

        return instance;
    }

    public void Update(RepoModel repo, LocalInstance instance, string name, DynamicForm instanceSettings)
    {
        instance.Name = name;
        instance.AdapterInstanceSettings = gameAdapterIndex.GetById(repo.AdapterId).SerializeInstanceSettings(instanceSettings);

        store.Save();
    }

    public void Delete(LocalInstance instance)
    {
        GetByRepoId(instance.RepoId).Remove(instance);

        store.Save();
    }
}
