using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

public class Repo
    : INotifyPropertyChanged, IDisposable
{
    private readonly RepoRepository _repoService;
    private readonly GameRepository _gameRepository;

    private ObservableCollectionSynchronizer<Game, Game, string> _gamesSynchronizer;


    public Repo(
        RepoMembershipDto repoMembershipDto,
        IGameAdapterIndex gameAdapterIndex,
        RepoRepository repoService,
        GameRepository gameRepository)
    {
        Adapter = gameAdapterIndex.GetById(GameAdapterId.Parse(repoMembershipDto.Repo.AdapterId)).WithBaseSettings(repoMembershipDto.Repo.AdapterConfiguration);
        _repoService = repoService;
        _gameRepository = gameRepository;
        Id = repoMembershipDto.Repo.Id;
        Name = repoMembershipDto.Repo.Name;
        Tag = repoMembershipDto.Repo.Tag;
        MembershipLevel = repoMembershipDto.MembershipLevel;
        AdapterConfiguration = repoMembershipDto.Repo.AdapterConfiguration;

        Games = [];

        _gamesSynchronizer = CreateGamesSynchronizer();
    }


    public event PropertyChangedEventHandler? PropertyChanged;


    public Guid Id { get; }
    public string Name { get; private set; }

    /// <summary>
    /// Four digits that separate this repo from another one called the same. Not observable and
    /// never reassigned: it follows the id, so a rename does not move it - see
    /// <see cref="Repos.RepoDisplay"/> for where it is drawn and where it is not.
    /// </summary>
    public string Tag { get; }

    public RepoMembershipLevel MembershipLevel { get; private set; }
    public ObservableCollection<Game> Games { get; }
    public IBaseGameAdapter Adapter { get; private set; }
    public GameIdentity Scope => Adapter.Scope;

    /// <summary>
    /// The base settings as the server last sent them. <see cref="Adapter"/> holds them parsed; this
    /// is kept beside it only so a background check can tell whether they have changed since.
    /// </summary>
    internal string AdapterConfiguration { get; private set; }

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
        AdapterConfiguration = dto.AdapterConfiguration;
        PropertyChanged?.Invoke(this, new(nameof(Adapter)));

        // The base settings carry the game discriminator, so editing them can move the repo to a
        // different set of games. The synchronizer's filter is fixed at construction, so it has
        // to be rebuilt rather than re-evaluated.
        if (Scope != previousScope)
        {
            _gamesSynchronizer.Dispose();
            _gamesSynchronizer = CreateGamesSynchronizer();

            PropertyChanged?.Invoke(this, new(nameof(Scope)));
        }
    }

    public void Dispose()
    {
        _gamesSynchronizer.Dispose();
    }

    internal RepoListEntry ToListEntry()
    {
        return new(Id, Name, MembershipLevel, AdapterConfiguration);
    }


    private ObservableCollectionSynchronizer<Game, Game, string> CreateGamesSynchronizer()
    {
        // Offered, not owned: a game is configured once per identity, so every repo targeting
        // that game lists the same one - and dropping it from this list must not dispose it.
        return new(
            source: _gameRepository.Games,
            target: Games,
            factory: x => x,
            keySelectorExpression: x => x.Name,
            comparer: NaturalOrder.Comparer,
            filter: x => x.Identity == Scope,
            disposeRemovedTargets: false);
    }
}
