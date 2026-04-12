using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Persistence;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

public class LocalInstance
    : INotifyPropertyChanged
{
    private readonly IGameAdapter _adapter;


    public LocalInstance(IGameAdapter adapter, Repo repo, PersistedLocalInstance persistedModel)
    {
        _adapter = adapter;

        Id = persistedModel.Id;
        Repo = repo;
        InstanceSettings = adapter.DeserializeInstanceSettings(persistedModel.AdapterInstanceSettings);
        PersistedModel = persistedModel;
    }

    public LocalInstance(IGameAdapter adapter, Repo repo, string name, DynamicForm instanceSettings)
    {
        _adapter = adapter;
        InstanceSettings = instanceSettings;

        Id = Guid.NewGuid();
        Repo = repo;
        PersistedModel = new PersistedLocalInstance()
        {
            Id = Id,
            Name = name,
            RepoId = repo.Id,
            AdapterInstanceSettings = _adapter.SerializeInstanceSettings(instanceSettings)
        };
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public Repo Repo { get; }

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

    public Task<IEnumerable<LocalMod>> GetInstalledMods(CancellationToken cancellationToken)
    {
        if (_adapter.ModAdapter is null)
        {
            throw UserFriendlyException.RepoNoModSupport();
        }

        return _adapter.ModAdapter.GetModsFromInstalled(InstanceSettings, cancellationToken);
    }
}
