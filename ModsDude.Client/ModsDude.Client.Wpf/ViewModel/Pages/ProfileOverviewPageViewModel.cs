using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Users;
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
/// <para>
/// <b>The savegames are read here and listed elsewhere.</b> A profile's current savegame is a fact about
/// the profile and belongs on its page; the savegames themselves stay on the one repo-level list, so
/// the past ones are a count and a link into that list rather than a second list of rows to keep true.
/// See docs/10-savegame-profile-binding.md#profile-page.
/// </para>
/// <para>
/// <b>But the current one can be acted on here.</b> Publish, check in and check out are the three
/// things somebody does with the savegame a profile is following, and they are the Saves list's own
/// flows - <see cref="SavegameFlowService"/> - with the current savegame's row rules deciding what is
/// enabled, so the two pages cannot come to disagree about when a check-out is allowed.
/// </para>
/// </remarks>
public partial class ProfileOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;
    private readonly ISavegamesClient _savegamesClient;
    private readonly CurrentUserService _currentUserService;
    private readonly SavegameFlowService _flowService;
    private readonly ShellNavigationService _navigation;
    private readonly DriftMonitor _driftMonitor;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    private ProfileModStatistics? _fetchedModStatistics;

    /// <summary>
    /// Every savegame in the repo, live and archived. This profile's are picked out of it, and the rest
    /// are what the check-out dialog names a savegame in the way by.
    /// </summary>
    private IReadOnlyList<SavegameDto> _fetchedSavegames = [];

    private string? _currentUserId;

    /// <summary>Whether the repo answered at all. Unknown is not the same as "no savegame here".</summary>
    private bool _savegamesUnreadable;


    public ProfileOverviewPageViewModel(
        Repo repo,
        ProfileDto profile,
        ProfileService profileService,
        ISavegamesClient savegamesClient,
        CurrentUserService currentUserService,
        SavegameFlowService flowService,
        ShellNavigationService navigation,
        DriftMonitor driftMonitor)
    {
        _repo = repo;
        _profile = profile;
        _profileService = profileService;
        _savegamesClient = savegamesClient;
        _currentUserService = currentUserService;
        _flowService = flowService;
        _navigation = navigation;
        _driftMonitor = driftMonitor;

        // Captured once, so that work still in flight after Dispose reads a cancelled token rather
        // than an ObjectDisposedException off the source it came from.
        _lifetime = _pageLifetime.Token;

        // Member, like the Saves list: publishing and claiming both write to the repo, and a guest is
        // not offered buttons that lead to a refusal.
        IsMember = repo.MembershipLevel >= RepoMembershipLevel.Member;

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

    /// <summary>Whether the savegame buttons are offered at all. A guest reads the card and nothing more.</summary>
    public bool IsMember { get; }

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

    /// <summary>
    /// The current savegame as the Saves list would draw it: the object that already knows whether it
    /// can be checked out here, whether it is held here and by whom, and how to say why not. Null where
    /// the profile has no current savegame.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent))]
    private SavegameListItemViewModel? _current;

    /// <summary>Set while a flow is running, and until the savegames have been read once.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private bool _isWorking = true;

    public bool HasArchivedWarning => ArchivedWarning is not null;
    public bool HasPastSavegames => PastSavegameCount > 0;

    public string PastSavegamesText => PastSavegameCount == 1
        ? "1 past savegame"
        : $"{PastSavegameCount} past savegames";

    /// <summary>
    /// Whether Check in and Check out are offered. A profile with no current savegame has nothing to
    /// claim or hand back, so it gets Publish and nothing else.
    /// </summary>
    public bool HasCurrent => Current is not null;

    /// <summary>
    /// Which of the two leads, the way the Saves row decides it: Check in where the save is yours and
    /// here to hand back, Check out otherwise - so the card always has one obvious thing to do.
    /// </summary>
    public bool ChecksInAsPrimary => Current is { CanCheckIn: true };

    public bool ChecksOutAsPrimary => Current is { CanCheckIn: false };

    public string CheckOutLabel => Current?.CheckOutLabel ?? "Check out";

    public string PublishToolTip =>
        $"Takes a save already on this machine and makes a savegame of it in {_repo.Name}, with '{_profile.Name}' picked as the mod list it follows.";

    /// <summary>
    /// The row's own tooltip - its refusal where there is one - plus the one refusal the row does not
    /// know: an archived savegame is not on the Saves list, so it is not checked out from anywhere.
    /// </summary>
    public string? CheckOutToolTip => Current switch
    {
        null => null,
        { Savegame.ArchivedAt: not null } current => $"'{current.Name}' is archived. Un-archive it from the repo's Archive first.",
        var current => current.CheckOutToolTip
    };

    /// <summary>
    /// Why there is nothing to check in, where there is not. The row never has to say it, because it
    /// only shows its button when there is.
    /// </summary>
    public string? CheckInToolTip => Current switch
    {
        null => null,
        { CanCheckIn: true } current => current.CheckInToolTip,
        { IsHoldUnreachable: true } current => current.UnreachableHoldNote,
        { IsHeldByMe: true } current => $"'{current.Name}' is checked out to you on another machine. Check it in from there.",
        { Holder: SavegameCheckoutDto holder } current => $"'{current.Name}' is checked out to {holder.User.DisplayName}. Only they can check it in.",
        var current => $"Nobody has '{current.Name}' checked out, so there is nothing to check in."
    };


    public void Dispose()
    {
        _pageLifetime.Cancel();

        _profileService.ProfileUpdated -= OnProfileUpdated;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _driftMonitor.Changed -= OnDriftChanged;

        _pageLifetime.Dispose();
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

    /// <summary>
    /// The Saves list's publish, opened on this profile. Every answer stays on offer in the dialog -
    /// this page only decides where it starts.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task Publish()
    {
        IsWorking = true;

        try
        {
            await _flowService.PublishAsync(_repo, _profile.Id, _ => ReloadSavegamesAsync(), _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanPublish() => IsWorking is false;

    /// <summary>
    /// The Saves list's check-out of the head snapshot, the same one its row offers.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckOut))]
    private async Task CheckOut()
    {
        if (Current is not SavegameListItemViewModel current)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _flowService.CheckOutAsync(
                _repo,
                current.Savegame,
                current.Savegame.Head?.Number ?? 0,
                SavegameCheckOutMode.CheckOut,
                _currentUserId,
                NameOf,
                ReloadSavegamesAsync,
                _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanCheckOut()
        => IsWorking is false && Current is { CanCheckOut: true, Savegame.ArchivedAt: null };

    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private async Task CheckIn()
    {
        if (Current is not { HeldHere: Game game } current)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _flowService.CheckInHeldAsync(game, current.Id, current.Name, ReloadSavegamesAsync, _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanCheckIn() => IsWorking is false && Current is { CanCheckIn: true };


    protected override async Task InitAsync()
    {
        _fetchedModStatistics = await _profileService.GetModStatistics(_repo.Id, _profile.Id, CancellationToken.None);
        _currentUserId = await ReadCurrentUserIdAsync();
        _fetchedSavegames = await LoadSavegamesAsync(CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        ModSummary = Describe(_fetchedModStatistics);

        DescribeSavegames();

        IsWorking = false;
    }

    /// <summary>
    /// Which "checked out to" is you. Absorbed: a card that cannot tell whose is whose still says who
    /// holds the save, and only the Check in button is lost to it.
    /// </summary>
    private async Task<string?> ReadCurrentUserIdAsync()
    {
        if (HasSavegames is false || IsMember is false)
        {
            return null;
        }

        try
        {
            return (await _currentUserService.Get(_lifetime)).Id;
        }
        catch (ApiException)
        {
            return null;
        }
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

            _savegamesUnreadable = false;

            return [.. live.Concat(archived)];
        }
        catch (ApiException)
        {
            _savegamesUnreadable = true;

            return [];
        }
    }

    /// <summary>
    /// After a publish, check-in or check-out: the card says what the repo says now.
    /// </summary>
    private async Task ReloadSavegamesAsync()
    {
        _fetchedSavegames = await LoadSavegamesAsync(_lifetime);

        DescribeSavegames();
    }

    /// <summary>
    /// Which savegame this profile is following, said the way the savegames list says it: current is the
    /// unmarked default and past is a count.
    /// </summary>
    private void DescribeSavegames()
    {
        if (_savegamesUnreadable)
        {
            CurrentSavegame = "This repo's saves could not be read just now.";

            // Nothing to check in or out of a savegame nobody can see. Publish stays: it does not
            // depend on this read, and the server supersedes whatever is there either way.
            SetCurrent(null);

            return;
        }

        if (HasSavegames is false)
        {
            return;
        }

        var savegames = _fetchedSavegames.Where(x => x.ProfileId == _profile.Id).ToList();

        PastSavegameCount = savegames.Count(x => x.SupersededAt is not null);

        var current = savegames.FirstOrDefault(x => x.SupersededAt is null);

        SetCurrent(current);

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

    private void SetCurrent(SavegameDto? savegame)
    {
        Current = savegame is null
            ? null
            : new SavegameListItemViewModel(savegame, _profile.Name, _currentUserId, IsMember, isAmbiguous: false);

        OfferCurrent();
    }

    /// <summary>
    /// Tells the current savegame what this machine can do with it, by the Saves list's own rules. Asked
    /// again whenever the folders might have moved, since an apply is what enables a check-out.
    /// </summary>
    private void OfferCurrent()
    {
        if (Current is SavegameListItemViewModel current)
        {
            _flowService.Offer(_repo, current, _flowService.ReadHost(_repo), NameOf);
        }

        OnPropertyChanged(nameof(ChecksInAsPrimary));
        OnPropertyChanged(nameof(ChecksOutAsPrimary));
        OnPropertyChanged(nameof(CheckOutLabel));
        OnPropertyChanged(nameof(CheckOutToolTip));
        OnPropertyChanged(nameof(CheckInToolTip));
        CheckOutCommand.NotifyCanExecuteChanged();
        CheckInCommand.NotifyCanExecuteChanged();
    }

    private string? NameOf(Guid savegameId)
        => _fetchedSavegames.FirstOrDefault(x => x.Id == savegameId)?.Name;

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
        OfferCurrent();
    }

    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor checks off the UI thread, and these rows are bound. The check-out buttons ride
        // along: the folder moving is what the drift is about, and it is also what enables them.
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            RefreshGames();
            OfferCurrent();
        });
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
                // Null: this page has a Savegames card of its own saying which savegame the profile
                // follows, which is the same fact from the end somebody reading a profile cares
                // about. The repo's Overview is where the hold is said as a fact about the machine.
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
