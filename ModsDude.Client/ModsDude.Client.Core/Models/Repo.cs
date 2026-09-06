using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

public class Repo
    : INotifyPropertyChanged, IDisposable
{
    private readonly RepoRepository _repoService;
    private readonly LocalInstanceRepository _localInstanceRepository;

    private ObservableCollectionSynchronizer<LocalInstance, LocalInstance, string> _instancesSynchronizer;


    public Repo(
        RepoMembershipDto repoMembershipDto,
        IGameAdapterIndex gameAdapterIndex,
        RepoRepository repoService,
        LocalInstanceRepository localInstanceRepository)
    {
        Adapter = gameAdapterIndex.GetById(GameAdapterId.Parse(repoMembershipDto.Repo.AdapterId)).WithBaseSettings(repoMembershipDto.Repo.AdapterConfiguration);
        _repoService = repoService;
        _localInstanceRepository = localInstanceRepository;
        Id = repoMembershipDto.Repo.Id;
        Name = repoMembershipDto.Repo.Name;
        MembershipLevel = repoMembershipDto.MembershipLevel;

        LocalInstances = [];

        _instancesSynchronizer = CreateInstancesSynchronizer();
    }


    public event PropertyChangedEventHandler? PropertyChanged;


    public Guid Id { get; }
    public string Name { get; private set; }
    public RepoMembershipLevel MembershipLevel { get; private set; }
    public ObservableCollection<LocalInstance> LocalInstances { get; }
    public IBaseGameAdapter Adapter { get; private set; }
    public InstanceScope Scope => Adapter.Scope;

    // TODO: Profiles


    public Task Update(string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        return _repoService.Update(this, name, baseSettings, cancellationToken);
    }

    /// <summary>
    /// Folds a server response into the live model rather than replacing it, so the menu entry bound
    /// to this repo - and whatever page is open under it - survives a rename.
    /// </summary>
    internal void Apply(RepoMembershipDto dto)
    {
        Apply(dto.Repo);

        if (MembershipLevel != dto.MembershipLevel)
        {
            MembershipLevel = dto.MembershipLevel;
            PropertyChanged?.Invoke(this, new(nameof(MembershipLevel)));
        }
    }

    internal void Apply(RepoDto dto)
    {
        var previousScope = Scope;

        if (Name != dto.Name)
        {
            Name = dto.Name;
            PropertyChanged?.Invoke(this, new(nameof(Name)));
        }

        Adapter = Adapter.WithBaseSettings(dto.AdapterConfiguration);
        PropertyChanged?.Invoke(this, new(nameof(Adapter)));

        // The base settings carry the game discriminator, so editing them can move the repo to a
        // different set of instances. The synchronizer's filter is fixed at construction, so it has
        // to be rebuilt rather than re-evaluated.
        if (Scope != previousScope)
        {
            _instancesSynchronizer.Dispose();
            _instancesSynchronizer = CreateInstancesSynchronizer();

            PropertyChanged?.Invoke(this, new(nameof(Scope)));
        }
    }

    public void Dispose()
    {
        _instancesSynchronizer.Dispose();
    }


    private ObservableCollectionSynchronizer<LocalInstance, LocalInstance, string> CreateInstancesSynchronizer()
    {
        // Offered, not owned: an instance belongs to a game, so every repo targeting that game
        // lists the same instances - and dropping one from this list must not dispose it.
        return new(
            source: _localInstanceRepository.Instances,
            target: LocalInstances,
            factory: x => x,
            keySelectorExpression: x => x.Name,
            comparer: NaturalOrder.Comparer,
            filter: x => x.Scope == Scope,
            disposeRemovedTargets: false);
    }
}
