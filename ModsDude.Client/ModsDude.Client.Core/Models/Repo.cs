using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

public class Repo
    : IDisposable
{
    private readonly IGameAdapter _adapter;
    private readonly RepoRepository _repoService;
    private readonly ObservableCollectionSynchronizer<LocalInstance, PersistedLocalInstance, Guid> _instancesSynchronizer;


    public Repo(
        RepoMembershipDto repoMembershipDto,
        IGameAdapterIndex gameAdapterIndex,
        RepoRepository repoService,
        LocalInstanceRepository localInstanceRepository)
    {
        _adapter = gameAdapterIndex.GetById(GameAdapterId.Parse(repoMembershipDto.Repo.AdapterId));
        _repoService = repoService;
        Id = repoMembershipDto.Repo.Id;
        Name = repoMembershipDto.Repo.Name;
        MembershipLevel = repoMembershipDto.MembershipLevel;
        BaseSettings = _adapter.DeserializeBaseSettings(repoMembershipDto.Repo.AdapterConfiguration);

        LocalInstances = new(localInstanceRepository.GetByRepoId(Id)
            .Select(x => new LocalInstance(_adapter, this, x)));

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
    public DynamicForm BaseSettings { get; private set; }
    public ObservableCollection<LocalInstance> LocalInstances { get; }
    public IGameAdapter Adapter => _adapter;

    // TODO: Profiles


    public Task Update(string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        Name = name;
        BaseSettings = baseSettings;
        return _repoService.Update(this, cancellationToken);
    }

    public void CreateLocalInstance(string name, DynamicForm instanceSettings)
    {
        var instance = new LocalInstance(_adapter, this, name, instanceSettings);
        LocalInstances.Add(instance);
    }

    public void DeleteLocalInstance(LocalInstance instance)
    {
        LocalInstances.Remove(instance);
    }

    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string folderPath, CancellationToken cancellationToken)
    {
        if (_adapter.ModAdapter is null)
        {
            throw UserFriendlyException.RepoNoModSupport();
        }

        return _adapter.ModAdapter.GetModsFromFolder(folderPath, cancellationToken);
    }

    public void Dispose()
    {
        _instancesSynchronizer.Dispose();
    }
}


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
