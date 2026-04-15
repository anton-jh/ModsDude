using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Persistence;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

public class LocalInstance
    : INotifyPropertyChanged
{
    public LocalInstance(IBaseGameAdapter baseAdapter, Repo repo, PersistedLocalInstance persistedModel)
    {
        Id = persistedModel.Id;
        Repo = repo;
        PersistedModel = persistedModel;
        InstanceSettings = baseAdapter.DeserializeInstanceSettings(persistedModel.AdapterInstanceSettings);
        Adapter = baseAdapter.WithInstanceSettings(InstanceSettings);
    }

    public LocalInstance(IBaseGameAdapter baseAdapter, Repo repo, string name, DynamicForm instanceSettings)
    {
        Id = Guid.NewGuid();
        Repo = repo;
        InstanceSettings = instanceSettings;
        PersistedModel = new PersistedLocalInstance()
        {
            Id = Id,
            Name = name,
            RepoId = repo.Id,
            AdapterInstanceSettings = instanceSettings.Serialize()
        };
        Adapter = baseAdapter.WithInstanceSettings(InstanceSettings);
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public Repo Repo { get; }
    public IInstanceGameAdapter Adapter { get; }

    public string Name
    {
        get => PersistedModel.Name;
        set
        {
            PersistedModel.Name = value;
            PropertyChanged?.Invoke(this, new(nameof(Name)));
        }
    }
    public DynamicForm InstanceSettings
    {
        get => field;
        set
        {
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(InstanceSettings)));
        }
    }
    internal PersistedLocalInstance PersistedModel { get; }


    public void Update(string name, DynamicForm instanceSettings)
    {
        Name = name;
        InstanceSettings = instanceSettings;
        PersistedModel.AdapterInstanceSettings = instanceSettings.Serialize();
    }
}
