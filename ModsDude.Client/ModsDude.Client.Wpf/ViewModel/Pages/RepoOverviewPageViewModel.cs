using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the repo looks like from here: the game it offers and how that installation stands, the
/// profiles it holds, and the caller's standing in it.
/// </summary>
public partial class RepoOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileService _profileService;
    private readonly MembershipService _membershipService;
    private readonly DriftMonitor _driftMonitor;
    private readonly ISavegameService _savegameService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly GameRepository _gameRepository;
    private readonly ProfileApplyService _applyService;
    private readonly IToastService _toasts;

    private int? _fetchedMemberCount;


    public RepoOverviewPageViewModel(
        Repo repo,
        ProfileService profileService,
        MembershipService membershipService,
        DriftMonitor driftMonitor,
        ISavegameService savegameService,
        SavegameBindingStore bindingStore,
        GameRepository gameRepository,
        ProfileApplyService applyService,
        IToastService toasts)
    {
        _repo = repo;
        _profileService = profileService;
        _membershipService = membershipService;
        _driftMonitor = driftMonitor;
        _savegameService = savegameService;
        _bindingStore = bindingStore;
        _gameRepository = gameRepository;
        _applyService = applyService;
        _toasts = toasts;

        Games = [];

        _repo.PropertyChanged += OnRepoPropertyChanged;
        _repo.Games.CollectionChanged += OnSourceCollectionChanged;
        _profileService.Profiles.CollectionChanged += OnSourceCollectionChanged;
        _driftMonitor.Changed += OnDriftChanged;

        // Which profile a game follows changes without the collection, the profiles or the drift
        // answer changing - deactivating a game with nothing wrong with it is exactly that - so the
        // "Set to ..." line would otherwise go on saying what it said.
        _gameRepository.GameChanged += OnGameChanged;

        // A savegame taken or handed back changes the holding line without touching a mod folder or
        // a profile, so nothing else here would say so.
        _bindingStore.BindingsChanged += OnBindingsChanged;

        RefreshGames();
    }


    public string RepoName => _repo.Name;
    public string Game => _repo.Adapter.DisplayName;
    public ObservableCollection<GameOverviewViewModel> Games { get; }

    public string MembershipSummary => _repo.MembershipLevel switch
    {
        RepoMembershipLevel.Admin => "You are an admin of this repo.",
        RepoMembershipLevel.Member => "You are a member of this repo.",
        _ => "You are a guest in this repo."
    };

    public string ProfileSummary => _profileService.Profiles.Count switch
    {
        0 => "No profiles yet.",
        1 => "1 profile.",
        var count => $"{count} profiles."
    };

    public bool HasGames => Games.Count > 0;
    public bool HasNoGames => Games.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemberSummary))]
    private string? _memberSummary;

    public bool HasMemberSummary => MemberSummary is not null;


    public void Dispose()
    {
        _repo.PropertyChanged -= OnRepoPropertyChanged;
        _repo.Games.CollectionChanged -= OnSourceCollectionChanged;
        _profileService.Profiles.CollectionChanged -= OnSourceCollectionChanged;
        _driftMonitor.Changed -= OnDriftChanged;
        _bindingStore.BindingsChanged -= OnBindingsChanged;
        _gameRepository.GameChanged -= OnGameChanged;
    }


    /// <summary>
    /// Stops the game following its profile and leaves the mod folders exactly as they are.
    /// </summary>
    [RelayCommand]
    private Task Deactivate(GameOverviewViewModel row, CancellationToken cancellationToken)
        => DeactivateAsync(row, clearMods: false, cancellationToken);

    /// <summary>Stops the game following its profile and takes every mod out of its folders too.</summary>
    [RelayCommand]
    private Task DeactivateAndClear(GameOverviewViewModel row, CancellationToken cancellationToken)
        => DeactivateAsync(row, clearMods: true, cancellationToken);

    /// <remarks>
    /// Not greyed while something else is applying or a savegame is held: the service refuses both
    /// with a sentence that says what to wait for or check in, which is a better answer on a page with
    /// no room for a reason than a button that just does not work.
    /// </remarks>
    private async Task DeactivateAsync(GameOverviewViewModel row, bool clearMods, CancellationToken cancellationToken)
    {
        var outcome = await _applyService.DeactivateAsync(_repo, row.Game, clearMods, progress: null, cancellationToken);

        _toasts.Show(outcome.Message, outcome.ToastSeverity);

        await _driftMonitor.CheckAsync();

        RefreshGames();
    }


    /// <summary>
    /// Reads the folders again, for somebody who has just changed something outside the app.
    /// </summary>
    /// <remarks>
    /// The game page's Re-check, which came here with the rest of its sidebar. Worth keeping: mods
    /// updated from inside the game are the commonest way a folder drifts, and the alternative to a
    /// button is waiting for the next window activation and wondering whether it ran.
    /// </remarks>
    [RelayCommand]
    private async Task Recheck()
    {
        await _driftMonitor.CheckAsync();

        RefreshGames();
    }


    protected override async Task InitAsync()
    {
        // Reading the member list needs Member, so for a guest there is simply nothing to say.
        if (_repo.MembershipLevel < RepoMembershipLevel.Member)
        {
            return;
        }

        _fetchedMemberCount = (await _membershipService.GetMembers(_repo.Id, CancellationToken.None)).Count;
    }

    protected override void OnInitCompleted()
    {
        MemberSummary = _fetchedMemberCount switch
        {
            null => null,
            1 => "You are its only member.",
            var count => $"{count} members."
        };
    }


    private void OnRepoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RepoName));
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(MembershipSummary));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGames();

        OnPropertyChanged(nameof(ProfileSummary));
    }

    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor checks off the UI thread, and these rows are bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGames);
    }

    /// <inheritdoc cref="OnDriftChanged"/>
    private void OnGameChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGames);
    }

    /// <summary>A savegame taken or handed back, here or anywhere else on this machine.</summary>
    /// <remarks>Dispatched for the same reason: a check-in completing is not guaranteed to be on the UI thread.</remarks>
    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGames);
    }

    private void RefreshGames()
    {
        // Every entry per game rather than the first of them: a game reaching three folders has an
        // entry each, and the row places them onto the folders they are about.
        var drifted = _driftMonitor.Drifted
            .GroupBy(x => x.Game.Identity)
            .ToDictionary(x => x.Key, IReadOnlyList<TargetDrift> (x) => [.. x]);

        Games.Clear();

        foreach (var game in _repo.Games)
        {
            Games.Add(new GameOverviewViewModel(
                game,
                _repo.Adapter,
                DescribeActiveProfile(game),
                DescribeHolding(game),
                drifted.GetValueOrDefault(game.Identity, [])));
        }

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(HasNoGames));
    }

    private string DescribeActiveProfile(Game game)
    {
        if (game.ActiveProfile is not ActiveProfile active)
        {
            return "No profile set";
        }

        // A game is offered by every repo targeting the same game, so the one it is currently
        // set to may well belong to a different repo than the one being looked at.
        if (active.RepoId != _repo.Id)
        {
            return "Set to a profile in another repo";
        }

        return _profileService.Profiles.FirstOrDefault(x => x.Id == active.ProfileId) is ProfileDto profile
            ? $"Set to '{profile.Name}'"
            : "Set to a profile that no longer exists";
    }

    /// <summary>
    /// What this game is holding, in one line, or null - nearly always - where it holds nothing with
    /// a mod list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked of <see cref="SavegameHoldRules"/> rather than worked out here.</b> The same questions
    /// decide whether the sync engine refuses an apply, and a second copy of them on this page is one
    /// that eventually disagrees with the surfaces that act on it.
    /// </para>
    /// <para>
    /// <b>It names the mod list rather than the savegame</b>, which is where this differs from the
    /// game page's version of the line. Naming the save cost a round trip per repo holding one, and
    /// the repo's Saves list is both where the name is and where anything can be done about it - so
    /// the half worth a line here is the half that explains why the folder will not move: a pinned
    /// revision.
    /// </para>
    /// </remarks>
    private string? DescribeHolding(Game game)
    {
        var held = _savegameService.GetBindings(game);
        var claiming = held.FirstOrDefault(x => x.ProfileId is not null);

        if (claiming.ProfileId is not Guid profileId)
        {
            return null;
        }

        var profile = _profileService.Profiles.FirstOrDefault(x => x.Id == profileId)?.Name ?? "a mod list";

        return SavegameHoldRules.RequiredRevision(held, profileId) is int pinned
            ? $"Holding a savegame that runs on '{profile}' rev {pinned}. Check it in from Saves to move this game forward."
            : $"Holding a savegame that follows '{profile}'.";
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoOverviewPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoOverviewPageViewModel>(serviceProvider, repo);
    }
}
