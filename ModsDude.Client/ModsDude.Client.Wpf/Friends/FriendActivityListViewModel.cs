using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Friends;

/// <summary>
/// One friend's game as a list draws it: who, which profile, what they last did with it, and the
/// button that puts this machine on the same thing.
/// </summary>
public sealed partial class FriendActivityRowViewModel : ObservableObject
{
    public FriendActivityRowViewModel(
        GameActivityDto activity,
        IFriendActivityEnvironment environment,
        AvatarViewModel avatar,
        bool showTag,
        DateTimeOffset now)
    {
        Model = activity;

        var availability = environment.CanFollow(activity);

        Name = activity.User.DisplayName;
        Tag = showTag ? $"#{activity.User.Tag}" : null;
        Avatar = avatar;

        Profile = activity.ProfileName;
        ProfileDetail = activity.PinnedRevision is int pinned ? $"rev {pinned}" : "";

        var what = activity.Kind is GameActivityKind.SavegameCheckedOut
            ? $"Checked out {(activity.SavegameName is string save ? $"'{save}'" : "a savegame")} {SavegameWording.Ago(activity.ChangedAt, now)}"
            : $"Switched to it {SavegameWording.Ago(activity.ChangedAt, now)}";

        // Only where it says something the first half did not: a re-apply since is somebody still
        // playing on it, and "switched 3 days ago" alone would read as somebody who has stopped.
        Summary = activity.TouchedAt - activity.ChangedAt > TimeSpan.FromMinutes(5)
            ? $"{what} · active {SavegameWording.Ago(activity.TouchedAt, now)}"
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

    public AvatarViewModel Avatar { get; }

    public string Profile { get; }

    public string ProfileLine => $"on '{Profile}'";

    /// <summary>
    /// The revision where they are held on one. Head goes unsaid, and so does the repo: the list is
    /// one repo's.
    /// </summary>
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


/// <summary>
/// The friends list on a repo's overview: who else is on which of that repo's profiles.
/// </summary>
/// <remarks>
/// <para>
/// <b>Drawn from <see cref="FriendActivityService"/>, never fetched here.</b> Opening a page asks it
/// to read again, and every page showing it redraws when any of them - or the watcher - does. So two
/// overviews opened a minute apart cannot disagree about what Alex is on.
/// </para>
/// <para>
/// Redrawn too when a game here changes, because whether the button is offered depends on what this
/// machine is on: following somebody turns their row's button into "You are on this too".
/// </para>
/// </remarks>
public sealed partial class FriendActivityListViewModel : ObservableObject, IDisposable
{
    private readonly IFriendActivityService _friends;
    private readonly IFriendActivityEnvironment _environment;
    private readonly IUserAvatarFactory _avatarFactory;
    private readonly IFriendFollowService _follow;
    private readonly IGameRepository _games;
    private readonly IToastService _toasts;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Guid _repoId;


    public FriendActivityListViewModel(
        IFriendActivityService friends,
        IFriendActivityEnvironment environment,
        IUserAvatarFactory avatarFactory,
        IFriendFollowService follow,
        IGameRepository games,
        IToastService toasts,
        ILogger<FriendActivityListViewModel> logger,
        TimeProvider time,
        Guid repoId)
    {
        _time = time;
        _friends = friends;
        _environment = environment;
        _avatarFactory = avatarFactory;
        _follow = follow;
        _games = games;
        _toasts = toasts;
        _logger = logger;
        _repoId = repoId;

        _friends.Changed += OnChanged;
        _games.GameChanged += OnChanged;

        Rebuild();
    }


    public ObservableCollection<FriendActivityRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>What an empty list says, once it is known to be empty rather than not yet read.</summary>
    public string? EmptyText => HasRows
        ? null
        : !_friends.HasLoaded
            ? CouldNotRead ? "Could not reach the server to see what your friends are on." : "Looking..."
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
        // Rows arrive most recently active first, which is the order they are drawn in.
        var rows = _friends.Rows
            .Where(x => x.RepoId == _repoId)
            .ToList();

        var ambiguous = UserDisplay.FindAmbiguous(rows.Select(x => x.User).DistinctBy(x => x.Id));

        Rows.Clear();

        var now = _time.GetUtcNow();

        foreach (var row in rows)
        {
            Rows.Add(new FriendActivityRowViewModel(row, _environment, _avatarFactory.Create(row.User), ambiguous.Contains(row.User.Id), now));
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
        public FriendActivityListViewModel Create(Guid repoId)
            => new(
                serviceProvider.GetRequiredService<IFriendActivityService>(),
                serviceProvider.GetRequiredService<IFriendActivityEnvironment>(),
                serviceProvider.GetRequiredService<IUserAvatarFactory>(),
                serviceProvider.GetRequiredService<IFriendFollowService>(),
                serviceProvider.GetRequiredService<IGameRepository>(),
                serviceProvider.GetRequiredService<IToastService>(),
                serviceProvider.GetRequiredService<ILogger<FriendActivityListViewModel>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                repoId);
    }
}
