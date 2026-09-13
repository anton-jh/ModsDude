using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the profile looks like from here: how many mods it pins, which games on this
/// machine are set to match it, whether each of them still does, and which savegame it is following.
/// </summary>
/// <remarks>
/// <b>The savegames are read here and listed elsewhere.</b> A profile's current savegame is a fact about
/// the profile and belongs on its page; the savegames themselves stay on the one repo-level list, so
/// the past ones are a count and a link into that list rather than a second list of rows to keep true.
/// See docs/10-savegame-profile-binding.md#profile-page.
/// </remarks>
public partial class ProfileOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;
    private readonly ISavegamesClient _savegamesClient;
    private readonly ShellNavigationService _navigation;
    private readonly DriftMonitor _driftMonitor;

    private int? _fetchedModCount;
    private IReadOnlyList<SavegameDto> _fetchedSavegames = [];

    /// <summary>Whether the repo answered at all. Unknown is not the same as "no savegame here".</summary>
    private bool _savegamesUnreadable;


    public ProfileOverviewPageViewModel(
        Repo repo,
        ProfileDto profile,
        ProfileService profileService,
        ISavegamesClient savegamesClient,
        ShellNavigationService navigation,
        DriftMonitor driftMonitor)
    {
        _repo = repo;
        _profile = profile;
        _profileService = profileService;
        _savegamesClient = savegamesClient;
        _navigation = navigation;
        _driftMonitor = driftMonitor;

        Games = [];

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

    [ObservableProperty]
    private string _modSummary = "Counting mods...";

    /// <summary>
    /// The savegame this profile is following, by name and with whoever holds it.
    /// </summary>
    /// <remarks>
    /// A profile with no current savegame is the ordinary starting state rather than a special one -
    /// every profile begins here, and one used only as a mod list stays here - so this says so plainly
    /// instead of reading like something missing.
    /// </remarks>
    [ObservableProperty]
    private string _currentSavegame = "Reading this profile's savegames...";

    /// <summary>
    /// The current savegame is archived, with the three ways out.
    /// </summary>
    /// <remarks>
    /// Archiving is the repo-wide visibility state and deliberately does <em>not</em> release a
    /// profile's slot, so the profile still has a current savegame and the next publish still supersedes
    /// it. That is a state somebody can be stuck in without a sentence saying what to do about it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArchivedWarning))]
    private string? _archivedWarning;

    /// <summary>How many savegames this profile has moved on from, for the link into the saves list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPastSavegames))]
    [NotifyPropertyChangedFor(nameof(PastSavegamesText))]
    private int _pastSavegameCount;

    public bool HasArchivedWarning => ArchivedWarning is not null;
    public bool HasPastSavegames => PastSavegameCount > 0;

    public string PastSavegamesText => PastSavegameCount == 1
        ? "1 past savegame"
        : $"{PastSavegameCount} past savegames";


    public void Dispose()
    {
        _profileService.ProfileUpdated -= OnProfileUpdated;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _driftMonitor.Changed -= OnDriftChanged;
    }


    /// <summary>
    /// Into the repo's saves list with the past savegames showing, which is where they live.
    /// </summary>
    /// <remarks>
    /// The toggle is turned on by the link rather than left to be found: a count that lands on a list
    /// filtering out the very rows it counted is a link that appears broken.
    /// </remarks>
    [RelayCommand]
    private async Task ShowPastSavegames()
    {
        if (await _navigation.GoToPastSavegamesAsync(_repo.Id) is false)
        {
            ArchivedWarning ??= "The repo's saves list could not be opened from here - pick it in the sidebar.";
        }
    }


    protected override async Task InitAsync()
    {
        _fetchedModCount = await _profileService.GetModCount(_repo.Id, _profile.Id, CancellationToken.None);
        _fetchedSavegames = await LoadSavegamesAsync(CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        ModSummary = Describe(_fetchedModCount);

        DescribeSavegames(_fetchedSavegames);
    }

    /// <summary>
    /// Both lists, because archived is not deleted: an archived savegame still holds its profile's
    /// slot, so a profile whose current savegame is archived still has one.
    /// </summary>
    /// <remarks>
    /// A failed read leaves the section saying so rather than saying the profile has no savegame. "No
    /// current savegame" is a real state with real consequences - the next publish creates one - and
    /// guessing it from a dropped connection would be a sentence somebody acts on.
    /// </remarks>
    private async Task<IReadOnlyList<SavegameDto>> LoadSavegamesAsync(CancellationToken cancellationToken)
    {
        if (HasSavegames is false)
        {
            return [];
        }

        try
        {
            var live = await _savegamesClient.GetSavegamesV1Async(_repo.Id, cancellationToken);
            var archived = await _savegamesClient.GetArchivedSavegamesV1Async(_repo.Id, cancellationToken);

            return [.. live.Concat(archived).Where(x => x.ProfileId == _profile.Id)];
        }
        catch (ApiException)
        {
            _savegamesUnreadable = true;

            return [];
        }
    }

    /// <summary>
    /// Which savegame this profile is following, said the way the savegames list says it: current is the
    /// unmarked default and past is a count.
    /// </summary>
    private void DescribeSavegames(IReadOnlyList<SavegameDto> savegames)
    {
        if (_savegamesUnreadable)
        {
            CurrentSavegame = "This repo's saves could not be read just now.";

            return;
        }

        if (HasSavegames is false)
        {
            return;
        }

        PastSavegameCount = savegames.Count(x => x.SupersededAt is not null);

        var current = savegames.FirstOrDefault(x => x.SupersededAt is null);

        if (current is null)
        {
            CurrentSavegame = PastSavegameCount > 0
                ? "No current savegame. The next save published to this profile becomes one."
                : "No savegame has been published to this profile yet.";

            ArchivedWarning = null;

            return;
        }

        var holder = current.Checkout is SavegameCheckoutDto checkout && checkout.Status is not SavegameCheckoutStatus.Ended
            ? $" Checked out to {checkout.User.DisplayName}."
            : " Nobody is holding it.";

        CurrentSavegame = $"'{current.Name}' is this profile's current savegame.{holder}";

        ArchivedWarning = current.ArchivedAt is null
            ? null
            : $"'{current.Name}' is this profile's current savegame and is archived. Un-archive it, delete it, or publish a new savegame.";
    }

    /// <summary>
    /// The revision is on the same line rather than a field of its own: it is what somebody says
    /// out loud when asking a teammate to look at the same list, and the History page is where it
    /// stops being a number and starts being a thing to act on.
    /// </summary>
    private string Describe(int? modCount)
    {
        var mods = modCount switch
        {
            0 or null => "No mods pinned yet",
            1 => "1 mod pinned",
            var count => $"{count} mods pinned"
        };

        return $"{mods} · revision {_profile.HeadRevision}.";
    }


    private void OnProfileUpdated(Guid profileId)
    {
        if (profileId == _profile.Id)
        {
            OnPropertyChanged(nameof(ProfileName));

            // The head moves when somebody saves or restores, and this page can be standing open
            // while that happens - from the History page next door, most obviously.
            ModSummary = Describe(_fetchedModCount);
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
                _repo.Adapter,
                "Set to this profile",
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
