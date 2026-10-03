using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Games;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// What the profile looks like from here: how many mods it pins, which games on this
/// machine are set to match it, whether each of them still does, and which savegames follow it.
/// </summary>
/// <remarks>
/// <b>The savegames are listed here and acted on elsewhere.</b> Each one leads to its row on the
/// repo's Saves list, which is the one place a savegame is checked out or in. Publish stays here,
/// opened on this profile.
/// </remarks>
public partial class ProfileOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly IProfileService _profileService;
    private readonly ISavegamesClient _savegamesClient;
    private readonly ISavegamePublishFlow _publishFlow;
    private readonly IShellNavigationService _navigation;
    private readonly IDriftMonitor _driftMonitor;
    private readonly ILogger<ProfileOverviewPageViewModel> _logger;
    private readonly TimeProvider _time;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    private ProfileModStatistics? _fetchedModStatistics;

    /// <summary>The repo's live savegames, or null where the repo could not be read.</summary>
    private IReadOnlyList<SavegameDto>? _fetchedSavegames;


    public ProfileOverviewPageViewModel(
        Repo repo,
        ProfileDto profile,
        IProfileService profileService,
        ISavegamesClient savegamesClient,
        ISavegamePublishFlow publishFlow,
        IShellNavigationService navigation,
        IDriftMonitor driftMonitor,
        ILogger<ProfileOverviewPageViewModel> logger,
        TimeProvider time)
    {
        _time = time;
        _repo = repo;
        _profile = profile;
        _profileService = profileService;
        _savegamesClient = savegamesClient;
        _publishFlow = publishFlow;
        _navigation = navigation;
        _driftMonitor = driftMonitor;
        _logger = logger;

        // Captured once, so that work still in flight after Dispose reads a cancelled token rather
        // than an ObjectDisposedException off the source it came from.
        _lifetime = _pageLifetime.Token;

        // Member, like the Saves list: publishing writes to the repo, and a guest is not offered a
        // button that leads to a refusal.
        IsMember = repo.MembershipLevel >= RepoMembershipLevel.Member;

        Games = [];
        Savegames = [];

        _profileService.ProfileUpdated += OnProfileUpdated;
        _repo.Games.CollectionChanged += OnGamesChanged;
        _driftMonitor.Changed += OnDriftChanged;

        RefreshGames();
    }


    public string ProfileName => _profile.Name;
    public string RepoName => _repo.Name;
    public ObservableCollection<GameOverviewViewModel> Games { get; }

    public bool HasGames => Games.Count > 0;
    public bool HasNoGames => Games.Count == 0;

    /// <summary>Whether this repo has savegames at all. The whole section is absent where it does not.</summary>
    public bool HasSavegames => _repo.Adapter.CanSupportSavegames;

    /// <summary>Whether Publish is offered. A guest reads the card and nothing more.</summary>
    public bool IsMember { get; }

    [ObservableProperty]
    private string _modSummary = "Counting mods...";

    /// <summary>This profile's savegames, the one played most recently first.</summary>
    public ObservableCollection<ProfileSavegameRowViewModel> Savegames { get; }

    /// <summary>
    /// What the section says in place of rows - still reading, none yet, or the repo could not be read
    /// - or null where there are rows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavegamesNote))]
    private string? _savegamesNote = "Reading this profile's savegames...";

    public bool HasSavegamesNote => SavegamesNote is not null;

    /// <summary>Set while a publish is running, and until the savegames have been read once.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _isWorking = true;

    public string PublishToolTip =>
        $"Takes a save already on this machine and makes a savegame of it in {_repo.Name}, with '{_profile.Name}' picked as the mod list it follows.";


    public void Dispose()
    {
        _pageLifetime.Cancel();

        _profileService.ProfileUpdated -= OnProfileUpdated;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _driftMonitor.Changed -= OnDriftChanged;

        _pageLifetime.Dispose();
    }


    /// <summary>
    /// The Saves list's publish, opened on this profile. Every answer stays on offer in the modal -
    /// this page only decides where it starts.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task Publish()
    {
        IsWorking = true;

        try
        {
            await _publishFlow.PublishAsync(_repo, _profile.Id, _ => ReloadSavegamesAsync(), _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanPublish() => IsWorking is false;


    protected override async Task InitAsync()
    {
        _fetchedModStatistics = await _profileService.GetModStatistics(_repo.Id, _profile.Id, CancellationToken.None);
        _fetchedSavegames = await LoadSavegamesAsync(CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        ModSummary = Describe(_fetchedModStatistics);

        DescribeSavegames();

        IsWorking = false;
    }

    /// <summary>
    /// A cancelled load is the expected outcome of navigating away - the drift notice's Review button
    /// can leave before the savegames have been read - not something to show the user an error modal
    /// about.
    /// </summary>
    protected override void OnInitFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        base.OnInitFailed(ex);
    }

    /// <summary>
    /// The live list only: an archived savegame is put away, and has no row on the Saves list to lead to.
    /// </summary>
    /// <remarks>
    /// A failed read leaves the section saying so rather than saying the profile has no savegame -
    /// "none yet" guessed from a dropped connection would be a sentence somebody acts on.
    /// </remarks>
    private async Task<IReadOnlyList<SavegameDto>?> LoadSavegamesAsync(CancellationToken cancellationToken)
    {
        if (HasSavegames is false)
        {
            return [];
        }

        try
        {
            return [.. await _savegamesClient.GetSavegamesV1Async(_repo.Id, cancellationToken)];
        }
        catch (ApiException exception)
        {
            _logger.LogWarning(exception, "Could not read the savegames of repo {RepoId} for profile {ProfileId}.", _repo.Id, _profile.Id);

            return null;
        }
    }

    /// <summary>After a publish: the card says what the repo says now.</summary>
    private async Task ReloadSavegamesAsync()
    {
        _fetchedSavegames = await LoadSavegamesAsync(_lifetime);

        DescribeSavegames();
    }

    private void DescribeSavegames()
    {
        Savegames.Clear();

        if (_fetchedSavegames is null)
        {
            SavegamesNote = "This repo's saves could not be read just now.";

            return;
        }

        var savegames = _fetchedSavegames
            .Where(x => x.ProfileId == _profile.Id)
            .OrderByDescending(x => x.Head?.Created)
            .ThenBy(x => x.Name, NaturalOrder.Comparer)
            .ThenBy(x => x.Id);

        var now = _time.GetUtcNow();

        foreach (var savegame in savegames)
        {
            Savegames.Add(new ProfileSavegameRowViewModel(savegame, now, OpenSavegameAsync));
        }

        SavegamesNote = Savegames.Count == 0
            ? "No savegame has been published to this profile yet."
            : null;
    }

    private async Task OpenSavegameAsync(Guid savegameId)
    {
        if (await _navigation.GoToSavegamesAsync(_repo.Id, savegameId) is false)
        {
            SavegamesNote = "The repo's saves list could not be opened from here - pick it in the sidebar.";
        }
    }

    /// <summary>
    /// The revision is on the same line rather than a field of its own: it is what somebody says
    /// out loud when asking a teammate to look at the same list, and the History page is where it
    /// stops being a number and starts being a thing to act on. The size is what the list would cost to
    /// download in full - what an apply fetches is less, and depends on what this machine already has.
    /// </summary>
    private string Describe(ProfileModStatistics? statistics)
    {
        var mods = statistics?.ModCount switch
        {
            0 or null => "No mods pinned yet",
            1 => "1 mod pinned",
            var count => $"{count} mods pinned"
        };

        var parts = new List<string> { mods };

        if (statistics is { ModCount: > 0 })
        {
            parts.Add(ByteSize.Describe(statistics.Bytes));
        }

        parts.Add($"revision {_profile.HeadRevision}");

        return string.Join(" · ", parts) + ".";
    }


    private void OnProfileUpdated(Guid profileId)
    {
        if (profileId == _profile.Id)
        {
            OnPropertyChanged(nameof(ProfileName));

            // The head moves when somebody saves or restores, and this page can be standing open
            // while that happens - from the History page next door, most obviously.
            ModSummary = Describe(_fetchedModStatistics);
        }
    }

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGames();
    }

    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor checks off the UI thread, and these rows are bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshGames);
    }

    /// <summary>
    /// Only the games actually set to this profile. A game the repo offers but that points
    /// somewhere else is the repo overview's business, not this page's.
    /// </summary>
    private void RefreshGames()
    {
        // Every entry per game rather than the first of them: a game reaching three folders has an
        // entry each, and the row places them onto the folders they are about.
        var drifted = _driftMonitor.Drifted
            .GroupBy(x => x.Game.Identity)
            .ToDictionary(x => x.Key, IReadOnlyList<TargetDrift> (x) => [.. x]);

        Games.Clear();

        var active = new ActiveProfile(_repo.Id, _profile.Id);

        foreach (var game in _repo.Games.Where(x => x.ActiveProfile == active))
        {
            Games.Add(new GameOverviewViewModel(
                game,
                GameInstallation.Read(game, _repo.Adapter, _logger),
                // Nulls: this page is the profile, and it lists the profile's savegames itself, which
                // is the same fact from the end somebody reading a profile cares about. The repo's
                // Overview is where the hold is said as a fact about the machine.
                activeProfile: null,
                holdingSummary: null,
                drifted.GetValueOrDefault(game.Identity, [])));
        }

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(HasNoGames));
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfileOverviewPageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfileOverviewPageViewModel>(serviceProvider, repo, profile);
    }
}
