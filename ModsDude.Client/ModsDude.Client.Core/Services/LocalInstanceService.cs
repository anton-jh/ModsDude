using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public class LocalInstanceService(
    StateStore store)
{
    public ObservableCollection<LocalInstance> Instances { get; } = [];


    public void Refresh()
    {
        var state = store.Get();
        Instances.Clear();
        foreach (var inst in state.LocalInstances)
        {
            Instances.Add(inst);
        }
    }

    public LocalInstance Create(Guid repoId, string name, DynamicForm instanceSettings)
    {
        var instance = new LocalInstance()
        {
            Id = Guid.NewGuid(),
            RepoId = repoId,
            Name = name,
            AdapterInstanceSettings = instanceSettings
        };

        var state = store.Get();
        state.LocalInstances.Add(instance);
        store.Save();

        Instances.Clear();
        foreach (var inst in state.LocalInstances)
        {
            Instances.Add(inst);
        }

        return instance;
    }

    public void Update(LocalInstance instance, string name, DynamicForm instanceSettings)
    {
        instance.Name = name;
        instance.AdapterInstanceSettings = instanceSettings;

        var state = store.Get();
        var old = state.LocalInstances.FirstOrDefault(x => x.Id == instance.Id);
        if (old is not null)
        {
            state.LocalInstances.Remove(old);
        }
        state.LocalInstances.Add(instance);
        store.Save();

        Instances.Clear();
        foreach (var inst in state.LocalInstances)
        {
            Instances.Add(inst);
        }
    }

    public void Delete(LocalInstance instance)
    {
        var state = store.Get();
        var existing = state.LocalInstances.FirstOrDefault(x => x.Id == instance.Id);
        if (existing is not null)
        {
            state.LocalInstances.Remove(existing);
        }
        store.Save();

        Instances.Clear();
        foreach (var inst in state.LocalInstances)
        {
            Instances.Add(inst);
        }
    }
}
