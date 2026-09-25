using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One friend's game as a list draws it: who, which profile, what they last did with it, and the
/// button that puts this machine on the same thing.
/// </summary>
public sealed partial class FriendActivityRowViewModel : ObservableObject
{
    public FriendActivityRowViewModel(GameActivityDto activity, IFriendActivityEnvironment environment, bool showTag)
    {
        Model = activity;

        var availability = environment.CanFollow(activity);

        Name = activity.User.DisplayName;
        Tag = showTag ? $"#{activity.User.Tag}" : null;
        Initial = UserDisplay.InitialFor(activity.User.DisplayName);
        AvatarColor = UserDisplay.ColorFor(activity.User.Tag);

        var repo = environment.DescribeRepo(activity.RepoId);
        var revision = activity.PinnedRevision is int pinned ? $"rev {pinned}" : null;

        Profile = activity.ProfileName;
        ProfileDetail = string.Join(" · ", new[] { repo, revision }.OfType<string>());

        var what = activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $"Checked out {(activity.SavegameName is string save ? $"'{save}'" : "a savegame")} {SavegameWording.Ago(activity.ChangedAt)}"
            : $"Switched to it {SavegameWording.Ago(activity.ChangedAt)}";

        // Only where it says something the first half did not: a re-apply since is somebody still
        // playing on it, and "switched 3 days ago" alone would read as somebody who has stopped.
        Summary = activity.TouchedAt - activity.ChangedAt > TimeSpan.FromMinutes(5)
            ? $"{what} · active {SavegameWording.Ago(activity.TouchedAt)}"
            : what;

        CanFollow = availability is FollowAvailability.Available;
        FollowLabel = FriendActivityRules.FollowLabel(activity);
        FollowBlocked = FriendActivityRules.FollowBlocked(activity, availability, environment);
    }


    public GameActivityDto Model { get; }

    public string Name { get; }

    /// <summary>The four digits, only where two friends in this list share a name.</summary>
    public string? Tag { get; }

    public bool HasTag => Tag is not null;

    public string Initial { get; }
    public string AvatarColor { get; }

    public string Profile { get; }

    public string ProfileLine => $"on '{Profile}'";

    /// <summary>The repo, and the revision where they are held on one. Head goes unsaid.</summary>
    public string ProfileDetail { get; }

    public bool HasProfileDetail => ProfileDetail.Length > 0;

    /// <summary>What they last did with it, and when.</summary>
    public string Summary { get; }

    public bool CanFollow { get; }
    public string FollowLabel { get; }

    /// <summary>Why there is no button, where there is not.</summary>
    public string? FollowBlocked { get; }

    public bool HasFollowBlocked => FollowBlocked is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isFollowing;

    public bool IsIdle => IsFollowing is false;
}


/// <summary>One game's friends, under the game's name.</summary>
public sealed record FriendActivityGroupViewModel(string Title, IReadOnlyList<FriendActivityRowViewModel> Rows);


/// <summary>
/// The friends list, for Home - every game, grouped - and for a repo's overview - that repo only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Drawn from <see cref="FriendActivityService"/>, never fetched here.</b> Opening a page asks it
/// to read again, and every page showing it redraws when any of them - or the watcher - does. So Home
/// and an overview opened a minute apart cannot disagree about what Alex is on.
/// </para>
/// <para>
/// Redrawn too when a game here changes, because whether the button is offered depends on what this
/// machine is on: following somebody turns their row's button into "You are on this too".
/// </para>
/// </remarks>
public sealed partial class FriendActivityListViewModel : ObservableObject, IDisposable
{
    private readonly FriendActivityService _friends;
    private readonly IFriendActivityEnvironment _environment;
    private readonly FriendFollowService _follow;
    private readonly GameRepository _games;
    private readonly IToastService _toasts;
    private readonly ILogger _logger;
    private readonly Guid? _onlyRepo;


    /// <param name="onlyRepo">One repo's friends, or everybody's where null.</param>
    public FriendActivityListViewModel(
        FriendActivityService friends,
        IFriendActivityEnvironment environment,
        FriendFollowService follow,
        GameRepository games,
        IToastService toasts,
        ILogger<FriendActivityListViewModel> logger,
        Guid? onlyRepo)
    {
        _friends = friends;
        _environment = environment;
        _follow = follow;
        _games = games;
        _toasts = toasts;
        _logger = logger;
        _onlyRepo = onlyRepo;

        _friends.Changed += OnChanged;
        _games.GameChanged += OnChanged;

        Rebuild();
    }


    public ObservableCollection<FriendActivityGroupViewModel> Groups { get; } = [];

    /// <summary>Whether the groups are worth a heading each - on Home, not on one repo's overview.</summary>
    public bool ShowGroupTitles => _onlyRepo is null;

    public bool HasRows => Groups.Count > 0;

    /// <summary>What an empty list says, once it is known to be empty rather than not yet read.</summary>
    public string? EmptyText => HasRows
        ? null
        : !_friends.HasLoaded
            ? CouldNotRead ? "Could not reach the server to see what your friends are on." : "Looking..."
            : _onlyRepo is null
                ? "Nobody you share a repo with has activated a profile in the last week."
                : "Nobody else in this repo has activated one of its profiles in the last week.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _couldNotRead;


    /// <summary>Reads the list again. Off the UI thread is fine: every redraw is dispatched.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            await _friends.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogInformation(exception, "Could not read which profiles friends are on.");

            Post(() => CouldNotRead = true);
        }
    }

    public void Dispose()
    {
        _friends.Changed -= OnChanged;
        _games.GameChanged -= OnChanged;
    }


    [RelayCommand]
    private async Task Follow(FriendActivityRowViewModel row)
    {
        row.IsFollowing = true;

        try
        {
            var (message, severity) = await _follow.FollowAsync(row.Model, CancellationToken.None);

            _toasts.Show(message, severity);
        }
        finally
        {
            row.IsFollowing = false;
        }
    }


    private void OnChanged(object? sender, EventArgs e) => Post(Rebuild);

    private static void Post(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);

    private void Rebuild()
    {
        var rows = _friends.Rows
            .Where(x => _onlyRepo is null || x.RepoId == _onlyRepo)
            .ToList();

        var ambiguous = UserDisplay.FindAmbiguous(rows.Select(x => x.User).DistinctBy(x => x.Id));

        // Rows arrive most recently active first, and a group is ordered by its most recent row - so
        // the game somebody is playing right now is the one at the top.
        var groups = rows
            .GroupBy(x => x.Game)
            .Select(x => new FriendActivityGroupViewModel(
                _environment.DescribeGame(x.Key),
                [.. x.Select(row => new FriendActivityRowViewModel(row, _environment, ambiguous.Contains(row.User.Id)))]))
            .ToList();

        Groups.Clear();

        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        if (_friends.HasLoaded)
        {
            CouldNotRead = false;
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(EmptyText));
    }


    public sealed class Factory(IServiceProvider serviceProvider)
    {
        public FriendActivityListViewModel Create(Guid? onlyRepo)
            => new(
                serviceProvider.GetRequiredService<FriendActivityService>(),
                serviceProvider.GetRequiredService<IFriendActivityEnvironment>(),
                serviceProvider.GetRequiredService<FriendFollowService>(),
                serviceProvider.GetRequiredService<GameRepository>(),
                serviceProvider.GetRequiredService<IToastService>(),
                serviceProvider.GetRequiredService<ILogger<FriendActivityListViewModel>>(),
                onlyRepo);
    }
}
